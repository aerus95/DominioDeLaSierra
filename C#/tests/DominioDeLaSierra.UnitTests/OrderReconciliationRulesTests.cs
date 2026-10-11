using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class OrderReconciliationRulesTests
{
    private static readonly Guid OrderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset ExpiresAt = new(2026, 10, 11, 12, 27, 18, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterExpiry = ExpiresAt.AddMinutes(13);

    [Fact]
    public void Confirms_a_held_reservation_after_expiry_when_the_test_payment_is_complete()
    {
        var reserved = OrderReconciliationRules.Decide(Ready());
        var confirming = OrderReconciliationRules.Decide(Ready() with
        {
            ReservationStatus = StockReservationStatus.Confirming
        });
        var confirmedHold = OrderReconciliationRules.Decide(Ready() with
        {
            ReservationStatus = StockReservationStatus.Confirmed
        });
        var lettersInsideTheIntent = OrderReconciliationRules.Decide(Ready(intentId: "pi_3MtwLiveHu7ix28a3tqPa"));

        Assert.Equal(OrderReconciliationDecision.Confirm, reserved.Decision);
        Assert.Equal(OrderReconciliationDecision.Confirm, confirming.Decision);
        Assert.Equal(OrderReconciliationDecision.Confirm, confirmedHold.Decision);
        Assert.Equal(OrderReconciliationDecision.Confirm, lettersInsideTheIntent.Decision);
        Assert.Null(reserved.Reason);
    }

    [Fact]
    public void Rejects_a_payment_that_is_not_a_completed_test_charge_for_this_order()
    {
        Assert.Equal(OrderReconciliationRules.IncompleteMessage, Reason(Ready(status: "expired")));
        Assert.Equal(OrderReconciliationRules.IncompleteMessage, Reason(Ready(paymentStatus: "unpaid")));
        Assert.Equal(OrderReconciliationRules.IncompleteMessage, Reason(Ready(intentStatus: "processing")));
        Assert.Equal(StripeCheckoutGuard.TestKeyMessage, Reason(Ready(intentId: "pi_live_3MtwBwLkdIwHu7ix28a3tqPa")));
        Assert.Equal(OrderReconciliationRules.IncompleteMessage, Reason(Ready(intentId: "seti_3MtwBwLkdIwHu7ix28a3tqPa")));
        Assert.Equal(StripeCheckoutGuard.TestKeyMessage, Reason(Ready(live: true)));
        Assert.Equal(StripeWebhookRules.AmountMessage, Reason(Ready(amount: 100)));
        Assert.Equal(StripeWebhookRules.CurrencyMessage, Reason(Ready(currency: "usd")));
        Assert.Equal(StripeWebhookRules.ReferenceMessage, Reason(Ready(reference: Guid.NewGuid().ToString("D"))));
        Assert.Equal(StripeWebhookRules.ReferenceMessage, Reason(Ready(metadata: null)));
        Assert.Equal(
            OrderReconciliationRules.SessionMessage,
            Reason(Ready(sessionId: "cs_test_other")));
        Assert.Equal(OrderReconciliationRules.UnavailableMessage, Reason(Ready() with { Session = null }));
    }

    [Fact]
    public void Rejects_a_released_reservation_and_accepts_a_repeated_settled_payment()
    {
        var released = OrderReconciliationRules.Decide(Ready() with
        {
            ReservationStatus = StockReservationStatus.Released,
            Now = ExpiresAt.AddMinutes(-1)
        });
        var settled = OrderReconciliationRules.Decide(Ready() with
        {
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            ReservationStatus = StockReservationStatus.Confirmed,
            StoredPaymentIntentId = "pi_3MtwBwLkdIwHu7ix28a3tqPa"
        });
        var again = OrderReconciliationRules.Decide(Ready() with
        {
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            ReservationStatus = StockReservationStatus.Confirmed,
            StoredPaymentIntentId = "pi_3MtwBwLkdIwHu7ix28a3tqPa"
        });
        var differentIntent = OrderReconciliationRules.Decide(Ready() with
        {
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            ReservationStatus = StockReservationStatus.Confirmed,
            StoredPaymentIntentId = "pi_3OtherIntent000000000000"
        });
        var finish = OrderReconciliationRules.Decide(Ready() with
        {
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            ReservationStatus = StockReservationStatus.Confirming,
            StoredPaymentIntentId = "pi_3MtwBwLkdIwHu7ix28a3tqPa"
        });

        Assert.Equal(OrderReconciliationDecision.Refuse, released.Decision);
        Assert.Equal(OrderReconciliationRules.ReleasedMessage, released.Reason);
        Assert.Equal(OrderReconciliationDecision.AlreadySettled, settled.Decision);
        Assert.Equal(OrderReconciliationDecision.AlreadySettled, again.Decision);
        Assert.Equal(StripeWebhookRules.IntentMessage, differentIntent.Reason);
        Assert.Equal(OrderReconciliationDecision.FinishHold, finish.Decision);
        Assert.Equal($"reconcile:{OrderId:N}:paid", OrderReconciliationRules.EventIdFor(OrderId));
        Assert.DoesNotContain("cs_", OrderReconciliationRules.EventIdFor(OrderId), StringComparison.Ordinal);
        Assert.DoesNotContain("pi_", OrderReconciliationRules.EventIdFor(OrderId), StringComparison.Ordinal);
    }

    private static string? Reason(OrderReconciliationContext context)
    {
        return OrderReconciliationRules.Decide(context).Reason;
    }

    private static OrderReconciliationContext Ready(
        string sessionId = "cs_test_a11YYufWQzNY63zp",
        string status = "complete",
        string paymentStatus = "paid",
        string intentId = "pi_3MtwBwLkdIwHu7ix28a3tqPa",
        string intentStatus = "succeeded",
        long amount = 1700,
        string currency = "eur",
        bool live = false,
        string? reference = null,
        string? metadata = "")
    {
        var orderReference = reference ?? OrderId.ToString("D");
        var metadataOrderId = metadata == "" ? OrderId.ToString("D") : metadata;
        return new OrderReconciliationContext(
            OrderId,
            "cs_test_a11YYufWQzNY63zp",
            null,
            OrderStatus.PendingPayment,
            PaymentStatus.Pending,
            1700,
            1700,
            StockReservationStatus.Reserved,
            ExpiresAt,
            AfterExpiry,
            new ObservedCheckoutSession(
                sessionId,
                status,
                paymentStatus,
                intentId,
                intentStatus,
                amount,
                currency,
                live,
                orderReference,
                metadataOrderId));
    }
}
