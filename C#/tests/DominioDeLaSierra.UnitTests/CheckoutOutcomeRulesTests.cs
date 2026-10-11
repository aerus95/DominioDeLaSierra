using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Checkout;

namespace DominioDeLaSierra.UnitTests;

public class CheckoutOutcomeRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Expired)]
    [InlineData(OrderStatus.Cancelled)]
    public void Presents_only_the_order_number_status_and_amount(OrderStatus status)
    {
        var outcome = CheckoutOutcomeRules.Present("DS-20261011-4133ca86", status, 1700, "EUR");

        Assert.Equal("DS-20261011-4133ca86", outcome.Number);
        Assert.Equal(status.ToString(), outcome.Status);
        Assert.Equal(1700, outcome.TotalCents);
        Assert.Equal("EUR", outcome.Currency);
    }

    [Fact]
    public void Refuses_an_outcome_that_is_not_a_known_euro_order()
    {
        Assert.Throws<CheckoutNotFoundException>(() => CheckoutOutcomeRules.Present("DS-1", (OrderStatus)99, 1700, "EUR"));
        Assert.Throws<CheckoutNotFoundException>(() => CheckoutOutcomeRules.Present("DS-1", OrderStatus.Paid, -1, "EUR"));
        Assert.Throws<CheckoutNotFoundException>(() => CheckoutOutcomeRules.Present("DS-1", OrderStatus.Paid, 1700, "usd"));
        Assert.Throws<CheckoutNotFoundException>(() => CheckoutOutcomeRules.Present(" ", OrderStatus.Paid, 1700, "EUR"));
    }

    [Fact]
    public void Keeps_the_stripe_return_address_free_of_secrets_and_session_identifiers()
    {
        var options = StripeCheckoutRequest.Create(new StripeCheckoutDraft(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            "DS-20261011-4133ca86",
            1700,
            [new StripeCheckoutLines.Line("Vino", 1, 1700)],
            DateTimeOffset.Parse("2026-10-11T12:30:00Z"),
            "http://localhost:4200/checkout/exito",
            "http://localhost:4200/checkout/cancelado"));

        Assert.Equal("http://localhost:4200/checkout/exito", options.SuccessUrl);
        Assert.Equal("http://localhost:4200/checkout/cancelado", options.CancelUrl);
        Assert.DoesNotContain("CHECKOUT_SESSION_ID", options.SuccessUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("cs_", options.SuccessUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_", options.SuccessUrl, StringComparison.Ordinal);
    }
}
