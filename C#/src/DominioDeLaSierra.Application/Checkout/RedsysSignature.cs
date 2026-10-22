using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DominioDeLaSierra.Application.Checkout;

/// <summary>
/// Firma HMAC_SHA512_V2 de la guía actual de redirección de Redsys.
/// La clave se recorta o se rellena a 16 caracteres, AES-CBC diversifica el pedido
/// y el HMAC firma el Base64URL usando la clave diversificada en Base64 estándar.
/// HMAC_SHA256_V1 queda fuera: no todos los terminales comparten algoritmo.
/// </summary>
public static class RedsysSignature
{
    public const string Version = "HMAC_SHA512_V2";
    public const string TestEndpoint = "https://sis-t.redsys.es:25443/sis/realizarPago";
    public const string EuroCurrency = "978";
    public const string AuthorizationType = "0";
    public const string InvalidSignatureMessage = "La firma de Redsys no es válida.";

    public static string Sign(string merchantParameters, string order, string secretKey)
    {
        var diversified = Convert.ToBase64String(Diversify(order, secretKey));
        var mac = HMACSHA512.HashData(Encoding.UTF8.GetBytes(diversified), Encoding.UTF8.GetBytes(merchantParameters));
        return Base64Url(mac);
    }

    public static bool Matches(string merchantParameters, string order, string secretKey, string? receivedSignature)
    {
        if (string.IsNullOrWhiteSpace(receivedSignature))
        {
            return false;
        }

        var expected = Sign(merchantParameters, order, secretKey);
        var actual = Canonical(receivedSignature);
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static byte[] Diversify(string order, string secretKey)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = NormalizeKey(secretKey);
        aes.IV = new byte[16];
        var plain = Encoding.UTF8.GetBytes(order);
        return aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
    }

    public static string EncodeParameters(IReadOnlyDictionary<string, string> parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return Base64Url(Encoding.UTF8.GetBytes(json));
    }

    public static JsonDocument DecodeParameters(string merchantParameters)
    {
        try
        {
            var json = Encoding.UTF8.GetString(FromBase64Url(merchantParameters));
            return JsonDocument.Parse(json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new RedsysSignatureException(InvalidSignatureMessage);
        }
    }

    private static byte[] NormalizeKey(string secretKey)
    {
        var text = secretKey ?? string.Empty;
        if (text.Length > 16)
        {
            text = text[..16];
        }
        else if (text.Length < 16)
        {
            text = text.PadRight(16, '0');
        }

        return Encoding.UTF8.GetBytes(text);
    }

    private static string Canonical(string signature)
    {
        var text = signature.Trim().Replace('+', '-').Replace('/', '_');
        return text.TrimEnd('=');
    }

    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        var text = value.Trim().Replace('-', '+').Replace('_', '/');
        var padding = text.Length % 4;
        if (padding > 0)
        {
            text = text.PadRight(text.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(text);
    }
}

public sealed class RedsysSignatureException : Exception
{
    public RedsysSignatureException(string message) : base(message)
    {
    }
}
