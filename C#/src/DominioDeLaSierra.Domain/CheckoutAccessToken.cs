using System.Security.Cryptography;
using System.Text;

namespace DominioDeLaSierra.Domain;

/// <summary>
/// Token de acceso al pago de un pedido de invitado. Se entrega una vez al crearlo
/// y en la base solo se guarda su hash SHA-256. El identificador del pedido no autoriza el pago.
/// </summary>
public static class CheckoutAccessToken
{
    public const int HashLength = 64;
    public const int MaxTokenLength = 128;

    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string token)
    {
        var text = OrderText.RequireToken(token, MaxTokenLength, "El acceso al pago");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static string RequireHash(string hash)
    {
        var text = hash?.Trim() ?? string.Empty;
        if (text.Length != HashLength || text.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("El acceso al pago no es válido.");
        }

        return text.ToLowerInvariant();
    }

    public static bool FixedEquals(string left, string right)
    {
        var first = Encoding.UTF8.GetBytes(left);
        var second = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(first, second);
    }
}
