using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Comprueba un webhook ya registrado. No crea ni modifica webhooks, pedidos ni capturas.
const string SandboxApiRoot = "https://api-m.sandbox.paypal.com";
const string ExpectedWebhookId = "9UC40709PV7468705";
const string ExpectedWebhookUrl = "https://mere-follow-arguments-chosen.trycloudflare.com/api/v1/checkout/paypal/webhook";
string[] ExpectedEvents =
[
    "PAYMENT.CAPTURE.COMPLETED",
    "PAYMENT.CAPTURE.DENIED",
    "PAYMENT.CAPTURE.PENDING",
    "PAYMENT.CAPTURE.REFUNDED"
];

var clientId = Environment.GetEnvironmentVariable("PayPal__ClientId");
var clientSecret = Environment.GetEnvironmentVariable("PayPal__ClientSecret");
if (!IsCredential(clientId) || !IsCredential(clientSecret))
{
    Console.Error.WriteLine("Faltan PayPal__ClientId o PayPal__ClientSecret en el entorno de este proceso.");
    return 2;
}

if (!Uri.TryCreate(SandboxApiRoot, UriKind.Absolute, out var root)
    || !string.Equals(root.GetLeftPart(UriPartial.Authority), SandboxApiRoot, StringComparison.OrdinalIgnoreCase)
    || IsLiveHost(root.Host))
{
    Console.Error.WriteLine("El host de la comprobación no es el Sandbox.");
    return 3;
}

using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
using var http = new HttpClient(handler)
{
    BaseAddress = root,
    Timeout = TimeSpan.FromSeconds(20)
};

var token = await TokenAsync(http, clientId!, clientSecret!);
if (token is null)
{
    return 1;
}

using var webhookRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/notifications/webhooks/" + ExpectedWebhookId);
webhookRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
webhookRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

HttpResponseMessage webhookResponse;
try
{
    webhookResponse = await http.SendAsync(webhookRequest);
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    Console.Error.WriteLine($"Webhook. Error de transporte ({exception.GetType().Name}).");
    return 1;
}

using (webhookResponse)
{
    var status = (int)webhookResponse.StatusCode;
    if (status is >= 300 and <= 399)
    {
        Console.WriteLine($"Webhook HTTP {status}. Redirección rechazada.");
        return 1;
    }

    var body = await webhookResponse.Content.ReadAsStringAsync();
    if (!webhookResponse.IsSuccessStatusCode)
    {
        Console.WriteLine($"Webhook HTTP {status}. Error.");
        return 1;
    }

    try
    {
        using var document = JsonDocument.Parse(body);
        var payload = document.RootElement;
        var id = payload.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String
            ? idProperty.GetString()
            : null;
        if (!string.Equals(id, ExpectedWebhookId, StringComparison.Ordinal))
        {
            Console.WriteLine($"Webhook HTTP {status}. Identificador: no coincide.");
            return 1;
        }

        var url = payload.TryGetProperty("url", out var urlProperty) && urlProperty.ValueKind == JsonValueKind.String
            ? urlProperty.GetString()
            : null;
        if (!string.Equals(url, ExpectedWebhookUrl, StringComparison.Ordinal))
        {
            Console.WriteLine($"Webhook HTTP {status}. URL: no coincide.");
            return 1;
        }

        if (!payload.TryGetProperty("event_types", out var eventsProperty) || eventsProperty.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($"Webhook HTTP {status}. Eventos: no coincide.");
            return 1;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in eventsProperty.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
            {
                Console.WriteLine($"Webhook HTTP {status}. Eventos: no coincide.");
                return 1;
            }

            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                Console.WriteLine($"Webhook HTTP {status}. Eventos: no coincide.");
                return 1;
            }
        }

        if (names.Count != ExpectedEvents.Length || ExpectedEvents.Any(expected => !names.Contains(expected)))
        {
            Console.WriteLine($"Webhook HTTP {status}. Eventos: no coincide.");
            return 1;
        }

        Console.WriteLine($"Webhook HTTP {status}. Identificador: coincide. URL: coincide. Eventos: coincide.");
        return 0;
    }
    catch (JsonException)
    {
        Console.WriteLine($"Webhook HTTP {status}. Respuesta no interpretable.");
        return 1;
    }
}

static async Task<string?> TokenAsync(HttpClient http, string clientId, string clientSecret)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
    var raw = Encoding.UTF8.GetBytes(clientId + ":" + clientSecret);
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials"
    });

    HttpResponseMessage response;
    try
    {
        response = await http.SendAsync(request);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine($"OAuth. Error de transporte ({exception.GetType().Name}).");
        return null;
    }

    using (response)
    {
        var status = (int)response.StatusCode;
        if (status is >= 300 and <= 399)
        {
            Console.WriteLine($"OAuth HTTP {status}. Redirección rechazada.");
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"OAuth HTTP {status}. Error.");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var payload = document.RootElement;
            var token = payload.TryGetProperty("access_token", out var tokenProperty) && tokenProperty.ValueKind == JsonValueKind.String
                ? tokenProperty.GetString()
                : null;
            var tokenType = payload.TryGetProperty("token_type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String
                ? typeProperty.GetString()
                : null;
            var expires = payload.TryGetProperty("expires_in", out var expiresProperty) && expiresProperty.TryGetInt32(out var seconds)
                ? seconds
                : 0;
            if (string.IsNullOrWhiteSpace(token) || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || expires <= 0)
            {
                Console.WriteLine($"OAuth HTTP {status}. Respuesta sin token utilizable.");
                return null;
            }

            Console.WriteLine($"OAuth HTTP {status}.");
            return token;
        }
        catch (JsonException)
        {
            Console.WriteLine($"OAuth HTTP {status}. Respuesta no interpretable.");
            return null;
        }
    }
}

static bool IsCredential(string? value)
{
    return value is not null && value.Length is >= 8 and <= 127 && !value.Any(char.IsWhiteSpace);
}

static bool IsLiveHost(string host)
{
    return host.Equals("api-m.paypal.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("api.paypal.com", StringComparison.OrdinalIgnoreCase);
}
