using System.Globalization;

namespace DominioDeLaSierra.Application.Common;

public static class CatalogNumbers
{
    private const int MaxRawLength = 32;

    public static bool TryParsePrice(string? text, out decimal value) =>
        TryParseAmount(text, CatalogLimits.MaxPrice, out value);

    public static bool TryParseVatRate(string? text, out decimal value) =>
        TryParseAmount(text, CatalogLimits.MaxVatRate, out value);

    public static bool IsStorablePrice(decimal value) =>
        IsStorableAmount(value, CatalogLimits.MaxPrice);

    public static bool IsStorableVatRate(decimal value) =>
        IsStorableAmount(value, CatalogLimits.MaxVatRate);

    public static bool IsStorableAmount(decimal value, decimal maximum)
    {
        if (value < 0 || value > maximum)
        {
            return false;
        }

        return decimal.Round(value, CatalogLimits.MoneyScale, MidpointRounding.AwayFromZero) == value;
    }

    public static bool TryParseInitialStock(string? text, out int quantity, out string? error)
    {
        quantity = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 10 || !IsDigits(trimmed))
        {
            error = "El stock inicial indicado no es válido.";
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "El stock inicial indicado no es válido.";
            return false;
        }

        if (parsed > InventoryLimits.MaxStockQuantity)
        {
            error = $"El stock inicial no puede superar {InventoryLimits.MaxStockQuantity} unidades.";
            return false;
        }

        quantity = parsed;
        return true;
    }

    public static bool TryParseStockQuantity(string? text, out int quantity, out string? error)
    {
        quantity = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "El stock indicado no es válido.";
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 10 || !IsDigits(trimmed))
        {
            error = "El stock indicado no es válido.";
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "El stock indicado no es válido.";
            return false;
        }

        if (parsed > InventoryLimits.MaxStockQuantity)
        {
            error = $"El stock no puede superar {InventoryLimits.MaxStockQuantity} unidades.";
            return false;
        }

        quantity = parsed;
        return true;
    }

    public static bool TryParseOptionalAlcohol(string? text, out decimal? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TryParseAmount(text, CatalogLimits.MaxAlcoholPercent, out var parsed))
        {
            error = "El grado alcohólico debe estar entre 0 y 99,99.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsDigits(string text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseAmount(string? text, decimal maximum, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > MaxRawLength || !TryNormalizeDecimal(trimmed, out var normalized))
        {
            return false;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (!IsStorableAmount(parsed, maximum))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryNormalizeDecimal(string text, out string normalized)
    {
        normalized = string.Empty;
        var separatorCount = 0;
        foreach (var character in text)
        {
            if (character is >= '0' and <= '9')
            {
                continue;
            }

            if (character is not (',' or '.'))
            {
                return false;
            }

            separatorCount++;
            if (separatorCount > 1)
            {
                return false;
            }
        }

        normalized = text.Replace(',', '.');
        var separator = normalized.IndexOf('.');
        if (separator < 0)
        {
            return normalized.Length > 0;
        }

        if (separator == 0)
        {
            return false;
        }

        var decimals = normalized.Length - separator - 1;
        return decimals is >= 1 and <= CatalogLimits.MoneyScale;
    }
}
