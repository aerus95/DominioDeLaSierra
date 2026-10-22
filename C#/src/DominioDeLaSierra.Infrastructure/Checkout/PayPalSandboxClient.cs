using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Cliente REST de Orders v2 contra api-m.sandbox.paypal.com. No tiene ruta hacia Live.
/// La verificación del webhook es local (CRC32 y SHA256withRSA), como indica la guía oficial,
/// porque reenviar el JSON puede cambiar el cuerpo y el simulador no admite el postback.
/// </summary>
public sealed class PayPalSandboxClient(HttpClient http, PayPalSettings settings) : IPayPalOrders, IPayPalCertificates
{
    private readonly SemaphoreSlim tokenGate = new(1, 1);
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt;

    public async Task<PayPalOrderSnapshot> CreateAsync(
        PayPalCreateCommand command,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            intent = PayPalRules.IntentCapture,
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    custom_id = PayPalProtocol.CustomId(command.OrderId),
                    invoice_id = command.OrderNumber,
                    amount = new { currency_code = PayPalRules.Currency, value = PayPalProtocol.FormatEuros(command.AmountCents) },
                    shipping = new
                    {
                        name = new { full_name = command.FullName },
                        address = new
                        {
                            address_line_1 = command.AddressLine,
                            admin_area_2 = command.City,
                            admin_area_1 = command.Province,
                            postal_code = command.PostalCode,
                            country_code = "ES"
                        }
                    }
                }
            },
            payment_source = new
            {
                paypal = new
                {
                    experience_context = new
                    {
                        brand_name = "Dominio de la Sierra",
                        locale = "es-ES",
                        landing_page = "LOGIN",
                        shipping_preference = "SET_PROVIDED_ADDRESS",
                        user_action = "PAY_NOW",
                        return_url = command.ReturnUrl,
                        cancel_url = command.CancelUrl,
                        payment_method_preference = "IMMEDIATE_PAYMENT_REQUIRED"
                    }
                }
            }
        };
        using var request = await AuthorizedAsync(HttpMethod.Post, "/v2/checkout/orders", cancellationToken);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return await SendOrderAsync(request, cancellationToken);
    }

    public async Task<PayPalOrderSnapshot> GetAsync(string paypalOrderId, CancellationToken cancellationToken = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, OrderPath(paypalOrderId), cancellationToken);
        return await SendOrderAsync(request, cancellationToken);
    }

    public async Task<PayPalOrderSnapshot> CaptureAsync(
        string paypalOrderId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Post, OrderPath(paypalOrderId) + "/capture", cancellationToken);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 422 && PayPalProtocol.AlreadyCaptured(body))
        {
            throw new PayPalAlreadyCapturedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalTransportException();
        }

        return Read(body);
    }

    public async Task<string> ReadPemAsync(string certificateUrl, CancellationToken cancellationToken = default)
    {
        if (!PayPalProtocol.IsSandboxCertificateUrl(certificateUrl))
        {
            throw new PayPalSignatureException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, certificateUrl);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalSignatureException();
        }

        var pem = await response.Content.ReadAsStringAsync(cancellationToken);
        if (pem.Length > 16_384 || pem.Contains("PRIVATE KEY", StringComparison.Ordinal) || !pem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        {
            throw new PayPalSignatureException();
        }

        return pem;
    }

    private async Task<PayPalOrderSnapshot> SendOrderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalTransportException();
        }

        return Read(body);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureSandbox();
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PayPalTransportException();
        }
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (!settings.IsOperational)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpiresAt)
        {
            return accessToken;
        }

        await tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (accessToken is not null && DateTimeOffset.UtcNow < accessTokenExpiresAt)
            {
                return accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var raw = Encoding.UTF8.GetBytes(settings.ClientId + ":" + settings.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalTransportException();
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.TryGetProperty("access_token", out var tokenProperty) ? tokenProperty.GetString() : null;
            var expires = document.RootElement.TryGetProperty("expires_in", out var expiresProperty) && expiresProperty.TryGetInt32(out var seconds)
                ? seconds
                : 0;
            var tokenType = document.RootElement.TryGetProperty("token_type", out var typeProperty) ? typeProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(token) || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || expires <= 0)
            {
                throw new PayPalTransportException();
            }

            accessToken = token;
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expires - 60));
            return token;
        }
        finally
        {
            tokenGate.Release();
        }
    }

    private void EnsureSandbox()
    {
        if (http.BaseAddress is null
            || !string.Equals(http.BaseAddress.GetLeftPart(UriPartial.Authority), PayPalRules.SandboxApiRoot, StringComparison.OrdinalIgnoreCase)
            || PayPalProtocol.IsLiveApiHost(http.BaseAddress.Host))
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }
    }

    private static string OrderPath(string paypalOrderId)
    {
        if (!PayPalProtocol.IsOrderId(paypalOrderId))
        {
            throw new PayPalTransportException();
        }

        return "/v2/checkout/orders/" + paypalOrderId;
    }

    private static PayPalOrderSnapshot Read(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return PayPalProtocol.ReadOrder(document.RootElement) ?? throw new PayPalTransportException();
        }
        catch (JsonException)
        {
            throw new PayPalTransportException();
        }
    }
}
