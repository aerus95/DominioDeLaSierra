using DominioDeLaSierra.Application.Checkout;

namespace DominioDeLaSierra.UnitTests;

public class CheckoutAccessRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Closed")]
    public void Production_stays_closed_for_everyone_when_access_is_closed(string? mode)
    {
        CheckoutAccessRules.Ensure("Production", mode);

        Assert.False(CheckoutAccessRules.Allows(mode, authenticatedTester: true));
        Assert.False(CheckoutAccessRules.Allows(mode, authenticatedTester: false));
    }

    [Fact]
    public void Tester_mode_accepts_only_an_authenticated_tester()
    {
        CheckoutAccessRules.Ensure("Production", "testers");

        Assert.True(CheckoutAccessRules.Allows("Testers", authenticatedTester: true));
        Assert.False(CheckoutAccessRules.Allows("Testers", authenticatedTester: false));
    }

    [Fact]
    public void Refuses_public_checkout_in_production_and_an_unknown_mode()
    {
        var publication = Assert.Throws<InvalidOperationException>(() => CheckoutAccessRules.Ensure("Production", "Public"));
        var unknown = Assert.Throws<InvalidOperationException>(() => CheckoutAccessRules.Ensure("Production", "live"));

        Assert.Equal(CheckoutAccessRules.PublicProductionMessage, publication.Message);
        Assert.Equal(CheckoutAccessRules.InvalidModeMessage, unknown.Message);
        Assert.False(CheckoutAccessRules.Allows("live", authenticatedTester: true));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Allows_public_checkout_only_outside_production(string environmentName)
    {
        CheckoutAccessRules.Ensure(environmentName, CheckoutAccessRules.Public);
        Assert.True(CheckoutAccessRules.Allows(CheckoutAccessRules.Public, authenticatedTester: false));
    }
}
