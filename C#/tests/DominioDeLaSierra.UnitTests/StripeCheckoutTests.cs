using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.UnitTests;

public class StripeCheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gives_thirty_minutes_plus_creation_slack_and_leaves_a_tail_on_the_reservation()
    {
        var reservation = Now.Add(StripeCheckoutExpiry.PaymentHold);
        var expires = StripeCheckoutExpiry.PaymentSessionExpiry(Now, reservation);

        Assert.Equal(Now.Add(StripeCheckoutExpiry.Minimum).Add(StripeCheckoutExpiry.SessionCreationSlack), expires);
        Assert.Equal(StripeCheckoutExpiry.ReservationTail, reservation - expires);
        Assert.True(expires - Now >= StripeCheckoutExpiry.Minimum);
        Assert.Equal(TimeSpan.FromMinutes(34), StripeCheckoutExpiry.PaymentHold);
    }

    [Fact]
    public void Does_not_stretch_the_session_past_the_reservation_tail()
    {
        var reservation = Now.AddMinutes(33);
        var expires = StripeCheckoutExpiry.PaymentSessionExpiry(Now, reservation);

        Assert.Equal(reservation.Subtract(StripeCheckoutExpiry.ReservationTail), expires);
        Assert.Equal(TimeSpan.FromMinutes(31), expires - Now);
        Assert.True(expires < reservation);
    }

    [Fact]
    public void Refuses_a_reservation_that_cannot_cover_thirty_minutes_and_the_tail()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StripeCheckoutExpiry.PaymentSessionExpiry(Now, Now.AddMinutes(31)));

        Assert.Equal(StripeCheckoutExpiry.OutsideStripeWindowMessage, exception.Message);
    }

    [Fact]
    public void Refuses_an_expired_reservation_without_extending_it()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StripeCheckoutExpiry.PaymentSessionExpiry(Now, Now.AddMinutes(-1)));

        Assert.Equal(StripeCheckoutExpiry.ExpiredMessage, exception.Message);
    }

    [Fact]
    public void Does_not_open_a_day_long_session_when_the_hold_is_longer()
    {
        var expires = StripeCheckoutExpiry.PaymentSessionExpiry(Now, Now.AddHours(48));

        Assert.Equal(Now.Add(StripeCheckoutExpiry.Minimum).Add(StripeCheckoutExpiry.SessionCreationSlack), expires);
        Assert.True(expires < Now.Add(StripeCheckoutExpiry.Maximum));
    }

    [Fact]
    public void Charges_the_persisted_gross_total_without_a_shipping_vat_rate()
    {
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-STRIPE-1",
            "stripe-lines",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, 1_890, 21m, null)],
            Now);

        var lines = StripeCheckoutLines.For(order);

        Assert.Equal(2, lines.Count);
        Assert.Equal(1_890, lines[0].UnitAmountCents);
        Assert.Equal(StripeCheckoutLines.ShippingName, lines[1].Name);
        Assert.Equal(800, lines[1].UnitAmountCents);
        Assert.Equal(order.TotalCents, lines.Sum(line => line.UnitAmountCents * line.Quantity));
        Assert.Null(order.ShippingVatRate);
    }

    [Fact]
    public void Stores_only_the_hash_of_the_checkout_access_token()
    {
        var token = CheckoutAccessToken.Create();
        var hash = CheckoutAccessToken.Hash(token);

        Assert.Equal(CheckoutAccessToken.HashLength, hash.Length);
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.True(CheckoutAccessToken.FixedEquals(hash, CheckoutAccessToken.Hash(token)));
        Assert.False(CheckoutAccessToken.FixedEquals(hash, CheckoutAccessToken.Hash(CheckoutAccessToken.Create())));
    }

    [Fact]
    public void Rejects_a_live_stripe_key()
    {
        Assert.Equal("sk_test_123", StripeCheckoutGuard.RequireTestSecret("  sk_test_123  "));
        Assert.Equal(
            StripeCheckoutGuard.TestKeyMessage,
            Assert.Throws<StripeCheckoutException>(() => StripeCheckoutGuard.RequireTestSecret("sk_live_123")).Message);
        Assert.Throws<StripeCheckoutException>(() => StripeCheckoutGuard.RequireTestSecret("sk_test_live_123"));
        Assert.Throws<StripeCheckoutException>(() => StripeCheckoutGuard.RequireTestSecret(null));
        Assert.Equal("whsec_test_local", StripeCheckoutGuard.RequireTestWebhookSecret(" whsec_test_local "));
        Assert.Throws<StripeCheckoutException>(() => StripeCheckoutGuard.RequireTestWebhookSecret("whsec_live_secret"));
        Assert.Throws<StripeCheckoutException>(() => StripeCheckoutGuard.RequireTestWebhookSecret("sk_test_123"));
    }

    [Fact]
    public void Rejects_a_live_checkout_session()
    {
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-STRIPE-2",
            "stripe-live",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, 1_890, 21m, null)],
            Now);
        var payment = order.AddPayment(Guid.NewGuid(), Now);

        Assert.Equal(
            "La sesión de pago no es de pruebas.",
            Assert.Throws<ArgumentException>(() => order.AssignHostedCheckout(
                payment.Id,
                "cs_live_secret",
                "https://checkout.stripe.com/c/pay/cs_live_secret",
                Now.AddMinutes(30))).Message);
        Assert.Null(payment.StripeCheckoutSessionId);
    }
}
