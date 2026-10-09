using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.UnitTests;

public class PeninsularSpainTests
{
    [Theory]
    [InlineData("01001")]
    [InlineData("08001")]
    [InlineData("28013")]
    [InlineData("37001")]
    [InlineData("50001")]
    public void Accepts_peninsular_province_prefixes(string postalCode)
    {
        Assert.Equal(postalCode, PeninsularSpain.RequirePostalCode(postalCode, "ES"));
        Assert.Equal(postalCode, PeninsularSpain.RequirePostalCode($" {postalCode} ", null));
    }

    [Theory]
    [InlineData("07001")]
    [InlineData("35001")]
    [InlineData("38001")]
    [InlineData("51001")]
    [InlineData("52001")]
    [InlineData("07001", "ES")]
    public void Rejects_balearic_canary_ceuta_and_melilla_codes(string postalCode, string? country = "es")
    {
        var exception = Assert.Throws<ArgumentException>(() => PeninsularSpain.RequirePostalCode(postalCode, country));
        Assert.Equal(PeninsularSpain.OutsideMessage, exception.Message);
    }

    [Theory]
    [InlineData("00000")]
    [InlineData("53001")]
    [InlineData("3700")]
    [InlineData("370011")]
    [InlineData("37A01")]
    public void Rejects_unknown_or_incomplete_postal_codes(string postalCode)
    {
        var exception = Assert.Throws<ArgumentException>(() => PeninsularSpain.RequirePostalCode(postalCode, "ES"));
        Assert.Equal(PeninsularSpain.InvalidPostalMessage, exception.Message);
    }

    [Fact]
    public void Rejects_a_foreign_country_even_with_a_peninsular_code()
    {
        var exception = Assert.Throws<ArgumentException>(() => PeninsularSpain.RequirePostalCode("37001", "FR"));
        Assert.Equal(PeninsularSpain.OutsideMessage, exception.Message);
    }

    [Fact]
    public void Converts_the_shelf_price_to_cents_without_assuming_shipping_vat()
    {
        Assert.Equal(1_890, OrderAmounts.PriceToCents(18.90m));
        Assert.Equal(10_000, OrderAmounts.PriceToCents(100m));
        Assert.Null(Order.CreatePending(
            Guid.NewGuid(),
            "DS-VAT",
            "vat-key",
            "Ana Rivas",
            "ana@example.com",
            "600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Vino", "VINO", ProductKind.Wine, 1, 1_890, 21m, null)],
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)).ShippingVatRate);
    }
}
