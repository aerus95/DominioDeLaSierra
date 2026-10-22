using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class ShippingTariffTests
{
    [Fact]
    public void Charges_six_euros_of_taxable_base_plus_the_product_rate_at_59_99()
    {
        var shipping = ShippingTariff.For(5_999, [21m]);

        Assert.Equal(600, shipping.TaxableBaseCents);
        Assert.Equal(21m, shipping.VatRate);
        Assert.Equal(126, shipping.VatCents);
        Assert.Equal(726, shipping.GrossCents);
        var (baseCents, vatCents) = OrderAmounts.SplitGross(shipping.GrossCents, 21m);
        Assert.Equal(600, baseCents);
        Assert.Equal(126, vatCents);
    }

    [Theory]
    [InlineData(6_000)]
    [InlineData(6_001)]
    public void Ships_for_free_from_sixty_euros_inclusive(long subtotalCents)
    {
        var shipping = ShippingTariff.For(subtotalCents, [21m]);

        Assert.Equal(0, shipping.GrossCents);
        Assert.Equal(21m, shipping.VatRate);
        Assert.Equal(0, shipping.TaxableBaseCents);
        Assert.Equal(0, shipping.VatCents);
    }

    [Fact]
    public void Uses_the_shared_rate_of_a_pack_and_a_bottle()
    {
        var shipping = ShippingTariff.For(4_890, [21m, 21m]);

        Assert.Equal(600, shipping.TaxableBaseCents);
        Assert.Equal(126, shipping.VatCents);
        Assert.Equal(726, shipping.GrossCents);
    }

    [Fact]
    public void Does_not_invent_a_rate_when_the_lines_differ_below_the_threshold()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ShippingTariff.For(5_999, [21m, 10m]));

        Assert.Equal(ShippingTariff.MixedRatesMessage, exception.Message);
    }

    [Fact]
    public void Leaves_a_free_mixed_rate_order_without_a_shipping_rate()
    {
        var shipping = ShippingTariff.For(6_000, [21m, 10m]);

        Assert.Equal(0, shipping.GrossCents);
        Assert.Null(shipping.VatRate);
        Assert.Null(shipping.TaxableBaseCents);
        Assert.Null(shipping.VatCents);
    }
}
