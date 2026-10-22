namespace DominioDeLaSierra.Domain;

/// <summary>
/// Importes de pedido en céntimos. Product.Price es PVP con IVA incluido.
/// La base y la cuota de los productos salen de ese bruto y del tipo guardado en cada línea.
/// El porte sale de ShippingTariff: 6,00 € de base más el tipo de las líneas, o 0 € desde 60,00 €.
/// </summary>
public static class OrderAmounts
{
    public const string Currency = "EUR";
    public const string CountryCode = "ES";
    public const long FreeShippingThresholdCents = ShippingTariff.FreeProductSubtotalCents;
    public const int MaxLineQuantity = 1_000_000;
    public const decimal MaxVatRate = 999.99m;

    public static readonly TimeSpan ReservationDuration = TimeSpan.FromMinutes(30);

    public static long PriceToCents(decimal price)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "El precio no es válido.");
        }

        var cents = decimal.Round(price * 100m, 0, MidpointRounding.AwayFromZero);
        return (long)cents;
    }

    public static (long TaxableBaseCents, long VatCents) SplitGross(long grossCents, decimal vatRate)
    {
        if (grossCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grossCents), "El importe no es válido.");
        }

        EnsureVatRate(vatRate);
        if (grossCents == 0)
        {
            return (0, 0);
        }

        var gross = grossCents / 100m;
        var divisor = 1m + (vatRate / 100m);
        var taxableBase = decimal.Round(gross / divisor, 2, MidpointRounding.AwayFromZero);
        var vat = gross - taxableBase;
        if (vat < 0)
        {
            throw new InvalidOperationException("El desglose de IVA no es válido.");
        }

        return (ToCents(taxableBase), ToCents(vat));
    }

    public static void EnsureVatRate(decimal vatRate)
    {
        if (vatRate < 0 || vatRate > MaxVatRate)
        {
            throw new ArgumentOutOfRangeException(nameof(vatRate), "El IVA indicado no es válido.");
        }

        if (decimal.Round(vatRate, 2, MidpointRounding.AwayFromZero) != vatRate)
        {
            throw new ArgumentOutOfRangeException(nameof(vatRate), "El IVA indicado no es válido.");
        }
    }

    public static long Multiply(long unitCents, int quantity)
    {
        if (unitCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCents), "El precio no es válido.");
        }

        if (quantity < 1 || quantity > MaxLineQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad no es válida.");
        }

        try
        {
            return checked(unitCents * quantity);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "El importe de la línea no es válido.");
        }
    }

    private static long ToCents(decimal amount)
    {
        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }
}
