namespace DominioDeLaSierra.Domain;

/// <summary>
/// Porte de la Península. El umbral usa el subtotal de productos ya valorado por el pedido.
/// Por debajo de 60,00 € la base imponible del transporte es 6,00 €.
/// Si todas las líneas comparten un tipo, el porte lleva ese mismo tipo: es un gasto accesorio
/// de la entrega y entra en la base de la operación (artículo 78.Dos.1º de la Ley 37/1992).
/// No se elige un porcentaje fijo. Varios tipos por debajo del umbral no se prorratean.
/// </summary>
public static class ShippingTariff
{
    public const long FreeProductSubtotalCents = 6_000;
    public const long TaxableBaseCents = 600;

    public const string MixedRatesMessage =
        "El IVA del transporte no está determinado porque el pedido tiene tipos de IVA distintos.";

    public const string InconsistentMessage = "El IVA del transporte no cuadra.";

    public readonly record struct Result(
        long GrossCents,
        decimal? VatRate,
        long? TaxableBaseCents,
        long? VatCents);

    public static Result For(long productSubtotalCents, IEnumerable<decimal> lineVatRates)
    {
        if (productSubtotalCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productSubtotalCents), "El subtotal no es válido.");
        }

        var rates = DistinctRates(lineVatRates);
        if (productSubtotalCents >= FreeProductSubtotalCents)
        {
            return rates.Length == 1
                ? new Result(0, rates[0], 0, 0)
                : new Result(0, null, null, null);
        }

        if (rates.Length != 1)
        {
            throw new InvalidOperationException(MixedRatesMessage);
        }

        var rate = rates[0];
        var vat = VatOnTaxableBase(TaxableBaseCents, rate);
        var gross = checked(TaxableBaseCents + vat);
        var (baseCents, vatCents) = OrderAmounts.SplitGross(gross, rate);
        if (baseCents != TaxableBaseCents || vatCents != vat)
        {
            throw new InvalidOperationException(InconsistentMessage);
        }

        return new Result(gross, rate, TaxableBaseCents, vat);
    }

    public static long VatOnTaxableBase(long taxableBaseCents, decimal vatRate)
    {
        if (taxableBaseCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taxableBaseCents), "La base imponible no es válida.");
        }

        OrderAmounts.EnsureVatRate(vatRate);
        var vatEuros = decimal.Round(taxableBaseCents / 100m * vatRate / 100m, 2, MidpointRounding.AwayFromZero);
        return (long)decimal.Round(vatEuros * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal[] DistinctRates(IEnumerable<decimal> lineVatRates)
    {
        ArgumentNullException.ThrowIfNull(lineVatRates);
        var rates = new List<decimal>();
        foreach (var rate in lineVatRates)
        {
            OrderAmounts.EnsureVatRate(rate);
            if (!rates.Contains(rate))
            {
                rates.Add(rate);
            }
        }

        if (rates.Count == 0)
        {
            throw new ArgumentException("El pedido debe incluir al menos un producto.");
        }

        return rates.ToArray();
    }
}
