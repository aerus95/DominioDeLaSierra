using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class StripeCheckoutFactsTests
{
    private static readonly Guid OrderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Keeps_an_open_session_ahead_of_an_expired_one()
    {
        var inspection = StripeCheckoutFacts.Combine(
            [
                Session("cs_test_old", "expired", "unpaid", "canceled"),
                Session("cs_test_open", "open", "unpaid", null)
            ],
            OrderId);

        Assert.Equal(StripeCheckoutFact.Open, inspection.Fact);
        Assert.Equal("cs_test_open", inspection.SessionId);
    }

    [Fact]
    public void Treats_a_succeeded_intent_as_paid_and_a_processing_intent_as_pending()
    {
        var paid = StripeCheckoutFacts.Classify(Session("cs_test_paid", "expired", "unpaid", "succeeded"), OrderId);
        var pending = StripeCheckoutFacts.Classify(Session("cs_test_wait", "complete", "unpaid", "processing"), OrderId);
        var expired = StripeCheckoutFacts.Classify(Session("cs_test_done", "expired", "unpaid", "canceled"), OrderId);
        var failedAsync = StripeCheckoutFacts.Classify(Session("cs_test_async", "complete", "unpaid", "requires_payment_method"), OrderId);

        Assert.Equal(StripeCheckoutFact.Paid, paid.Fact);
        Assert.Equal(StripeCheckoutFact.Pending, pending.Fact);
        Assert.Equal(StripeCheckoutFact.ExpiredUnpaid, expired.Fact);
        Assert.Equal(StripeCheckoutFact.ExpiredUnpaid, failedAsync.Fact);
    }

    [Fact]
    public void Keeps_stock_when_the_session_is_live_or_the_amounts_disagree()
    {
        var live = StripeCheckoutFacts.Classify(Session("cs_test_live_mode", "complete", "paid", "succeeded", live: true), OrderId);
        var conflict = StripeCheckoutFacts.Combine(
            [
                Session("cs_test_a", "complete", "paid", "succeeded", amount: 1000),
                Session("cs_test_b", "complete", "paid", "succeeded", amount: 2000)
            ],
            OrderId);

        Assert.Equal(StripeCheckoutFact.Contradiction, live.Fact);
        Assert.Equal(StripeCheckoutFact.Contradiction, conflict.Fact);
        Assert.Equal(StripeWebhookRules.AmountMessage, conflict.AttentionReason);
    }

    [Fact]
    public void Waits_after_expiry_when_the_session_has_not_been_stored()
    {
        var expiry = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var inside = expiry.Add(ReservationSweepRules.MissingSessionGrace).AddSeconds(-1);
        var outside = expiry.Add(ReservationSweepRules.MissingSessionGrace);

        Assert.True(ReservationSweepRules.WaitForMissingSession(false, StripeCheckoutFact.NotFound, inside, expiry));
        Assert.False(ReservationSweepRules.WaitForMissingSession(false, StripeCheckoutFact.NotFound, outside, expiry));
        Assert.False(ReservationSweepRules.WaitForMissingSession(true, StripeCheckoutFact.NotFound, inside, expiry));
        Assert.False(ReservationSweepRules.WaitForMissingSession(false, StripeCheckoutFact.Paid, inside, expiry));
    }

    [Fact]
    public void Rejects_a_paid_session_whose_amount_does_not_match_the_reserved_order()
    {
        var reason = PaidSessionRules.Attention(
            100,
            "eur",
            "pi_test_ok",
            OrderId.ToString("D"),
            OrderId.ToString("D"),
            OrderStatus.PendingPayment,
            PaymentStatus.Pending,
            4580,
            4580,
            OrderId,
            StockReservationStatus.Reserved);

        Assert.Equal(StripeWebhookRules.AmountMessage, reason);
    }

    private static ObservedCheckoutSession Session(
        string id,
        string status,
        string paymentStatus,
        string? intentStatus,
        bool live = false,
        long amount = 4580)
    {
        return new ObservedCheckoutSession(
            id,
            status,
            paymentStatus,
            "pi_test_intent",
            intentStatus,
            amount,
            "eur",
            live,
            OrderId.ToString("D"),
            OrderId.ToString("D"));
    }
}
