using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// PayPal solo opera en Sandbox. Client ID, Secret y Webhook ID llegan por configuración
/// (PayPal__ClientId, PayPal__ClientSecret, PayPal__WebhookId) y no se escriben en el registro.
/// Live no es operativo. El retorno puede ser localhost o un HTTPS público; nunca un host de PayPal.
/// Sin cualquiera de estos datos, el catálogo no anuncia PayPal.
/// </summary>
public sealed class PayPalSettings
{
    private PayPalSettings(
        bool operational,
        string clientId,
        string clientSecret,
        string webhookId,
        string returnUrl,
        string cancelUrl)
    {
        IsOperational = operational;
        ClientId = clientId;
        ClientSecret = clientSecret;
        WebhookId = webhookId;
        ReturnUrl = returnUrl;
        CancelUrl = cancelUrl;
    }

    public bool IsOperational { get; }

    public string ClientId { get; }

    public string ClientSecret { get; }

    public string WebhookId { get; }

    public string ReturnUrl { get; }

    public string CancelUrl { get; }

    public static PayPalSettings Read(IConfiguration configuration)
    {
        var enabled = string.Equals(configuration["PayPal:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        var environment = configuration["PayPal:Environment"]?.Trim() ?? string.Empty;
        var clientId = configuration["PayPal:ClientId"]?.Trim() ?? string.Empty;
        var secret = configuration["PayPal:ClientSecret"] ?? string.Empty;
        var webhookId = configuration["PayPal:WebhookId"]?.Trim() ?? string.Empty;
        var returnUrl = configuration["PayPal:ReturnUrl"]?.Trim() ?? string.Empty;
        var cancelUrl = configuration["PayPal:CancelUrl"]?.Trim() ?? string.Empty;
        var sandbox = string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment, "Live", StringComparison.OrdinalIgnoreCase);
        var operational = enabled
            && sandbox
            && IsCredential(clientId)
            && IsCredential(secret)
            && IsCredential(webhookId)
            && IsReturnUrl(returnUrl, "/checkout/exito")
            && IsReturnUrl(cancelUrl, "/checkout/cancelado");
        return new PayPalSettings(
            operational,
            operational ? clientId : string.Empty,
            operational ? secret : string.Empty,
            operational ? webhookId : string.Empty,
            operational ? returnUrl : string.Empty,
            operational ? cancelUrl : string.Empty);
    }

    private static bool IsCredential(string value)
    {
        return value.Length is >= 8 and <= 127 && !value.Any(char.IsWhiteSpace);
    }

    private static bool IsReturnUrl(string value, string path)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.Equals(url.AbsolutePath, path, StringComparison.Ordinal)
            || url.Host.EndsWith("paypal.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var loopback = url.Host is "localhost" or "127.0.0.1";
        return url.Scheme == "https" || loopback;
    }
}
