using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Redsys solo opera en el entorno de pruebas y con HMAC_SHA512_V2.
/// La clave llega por configuración (Redsys__SecretKey) y no se escribe en el registro.
/// </summary>
public sealed class RedsysSettings
{
    private RedsysSettings(
        bool operational,
        string merchantCode,
        string terminal,
        string secretKey,
        string merchantUrl,
        string urlOk,
        string urlKo)
    {
        IsOperational = operational;
        MerchantCode = merchantCode;
        Terminal = terminal;
        SecretKey = secretKey;
        MerchantUrl = merchantUrl;
        UrlOk = urlOk;
        UrlKo = urlKo;
    }

    public bool IsOperational { get; }

    public string MerchantCode { get; }

    public string Terminal { get; }

    public string SecretKey { get; }

    public string MerchantUrl { get; }

    public string UrlOk { get; }

    public string UrlKo { get; }

    public static RedsysSettings Read(IConfiguration configuration)
    {
        var enabled = string.Equals(configuration["Redsys:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        var environment = configuration["Redsys:Environment"]?.Trim() ?? string.Empty;
        var version = configuration["Redsys:SignatureVersion"]?.Trim() ?? string.Empty;
        var merchant = configuration["Redsys:MerchantCode"]?.Trim() ?? string.Empty;
        var terminal = configuration["Redsys:Terminal"]?.Trim() ?? string.Empty;
        var secret = configuration["Redsys:SecretKey"] ?? string.Empty;
        var merchantUrl = configuration["Redsys:MerchantUrl"]?.Trim() ?? string.Empty;
        var urlOk = configuration["Redsys:UrlOk"]?.Trim() ?? string.Empty;
        var urlKo = configuration["Redsys:UrlKo"]?.Trim() ?? string.Empty;
        var test = string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase);
        var operational = enabled
            && test
            && string.Equals(version, RedsysSignature.Version, StringComparison.Ordinal)
            && merchant.Length == 9
            && merchant.All(char.IsDigit)
            && terminal.Length is >= 1 and <= 3
            && terminal.All(char.IsDigit)
            && secret.Length > 0
            && !secret.Any(char.IsWhiteSpace)
            && IsReturnUrl(merchantUrl)
            && IsReturnUrl(urlOk)
            && IsReturnUrl(urlKo);
        return new RedsysSettings(
            operational,
            operational ? merchant : string.Empty,
            operational ? terminal : string.Empty,
            operational ? secret : string.Empty,
            operational ? merchantUrl : string.Empty,
            operational ? urlOk : string.Empty,
            operational ? urlKo : string.Empty);
    }

    private static bool IsReturnUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var url)
            && url.Scheme is "https" or "http"
            && string.IsNullOrEmpty(url.UserInfo)
            && url.Host is not "sis.redsys.es" and not "sis-t.redsys.es";
    }
}
