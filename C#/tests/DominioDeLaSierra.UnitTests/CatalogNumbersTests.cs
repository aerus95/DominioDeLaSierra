using DominioDeLaSierra.Application.Common;

namespace DominioDeLaSierra.UnitTests;

public class CatalogNumbersTests
{
    [Theory]
    [InlineData("18,90")]
    [InlineData("18.90")]
    public void Accepts_price_with_comma_or_dot(string text)
    {
        var parsed = CatalogNumbers.TryParsePrice(text, out var value);

        Assert.True(parsed);
        Assert.Equal(18.90m, value);
    }

    [Theory]
    [InlineData("18,999")]
    [InlineData("1.890")]
    [InlineData("-1")]
    [InlineData("10000000000.00")]
    public void Rejects_ambiguous_negative_or_oversized_prices(string text)
    {
        Assert.False(CatalogNumbers.TryParsePrice(text, out _));
    }

    [Fact]
    public void Accepts_the_maximum_price()
    {
        var parsed = CatalogNumbers.TryParsePrice("9999999999.99", out var value);

        Assert.True(parsed);
        Assert.Equal(CatalogLimits.MaxPrice, value);
    }

    [Fact]
    public void Accepts_a_vat_rate_inside_the_range()
    {
        var parsed = CatalogNumbers.TryParseVatRate("21,00", out var value);

        Assert.True(parsed);
        Assert.Equal(21.00m, value);
    }

    [Fact]
    public void Rejects_a_vat_rate_above_the_maximum()
    {
        Assert.False(CatalogNumbers.TryParseVatRate("1000", out _));
    }

    [Fact]
    public void Accepts_alcohol_up_to_99_99()
    {
        var parsed = CatalogNumbers.TryParseOptionalAlcohol("99,99", out var value, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(99.99m, value);
    }

    [Fact]
    public void Rejects_alcohol_above_99_99()
    {
        var parsed = CatalogNumbers.TryParseOptionalAlcohol("100", out var value, out var error);

        Assert.False(parsed);
        Assert.Null(value);
        Assert.Equal("El grado alcohólico debe estar entre 0 y 99,99.", error);
    }
}
