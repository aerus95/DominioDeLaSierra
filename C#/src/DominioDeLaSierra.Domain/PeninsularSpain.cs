namespace DominioDeLaSierra.Domain;

/// <summary>
/// Destino de envío dentro de España peninsular.
/// Correos asigna cinco dígitos: los dos primeros son la provincia, del 01 (Álava) al 50 (Zaragoza),
/// más 51 (Ceuta) y 52 (Melilla). El país ES no basta: se excluyen por prefijo
/// 07 Baleares, 35 Las Palmas, 38 Santa Cruz de Tenerife, 51 Ceuta y 52 Melilla.
/// </summary>
public static class PeninsularSpain
{
    public const string OutsideMessage = "Solo enviamos a España peninsular. Baleares, Canarias, Ceuta y Melilla quedan fuera de este envío.";
    public const string InvalidPostalMessage = "El código postal no es válido.";

    private static readonly HashSet<int> ExcludedProvincePrefixes = [7, 35, 38, 51, 52];

    public static string RequirePostalCode(string? postalCode, string? countryCode)
    {
        if (!string.IsNullOrWhiteSpace(countryCode)
            && !string.Equals(countryCode.Trim(), OrderAmounts.CountryCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(OutsideMessage);
        }

        var text = postalCode?.Trim() ?? string.Empty;
        if (text.Length != 5 || !IsDigits(text))
        {
            throw new ArgumentException(InvalidPostalMessage);
        }

        var prefix = int.Parse(text[..2], System.Globalization.CultureInfo.InvariantCulture);
        if (prefix is < 1 or > 52)
        {
            throw new ArgumentException(InvalidPostalMessage);
        }

        if (ExcludedProvincePrefixes.Contains(prefix))
        {
            throw new ArgumentException(OutsideMessage);
        }

        return text;
    }

    private static bool IsDigits(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
