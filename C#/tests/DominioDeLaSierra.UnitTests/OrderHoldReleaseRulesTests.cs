using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class OrderHoldReleaseRulesTests
{
    private static readonly Guid OrderId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly DateTimeOffset ExpiresAt = new(2026, 10, 11, 12, 33, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterExpiry = ExpiresAt.AddMinutes(20);

    [Fact]
    public void Releases_an_expired_unpaid_hold_and_ignores_a_repeated_release()
    {
        var release = OrderHoldReleaseRules.Decide(Ready());
        var missingIntent = OrderHoldReleaseRules.Decide(Ready(intentStatus: null));
        var settled = OrderHoldReleaseRules.Decide(Ready() with
        {
            OrderStatus = OrderStatus.Expired,
            PaymentStatus = PaymentStatus.Cancelled,
            ReservationStatus = StockReservationStatus.Released
        });
        var finish = OrderHoldReleaseRules.Decide(Ready() with
        {
            ReservationStatus = StockReservationStatus.Released
        });

        Assert.Equal(OrderHoldReleaseDecision.Release, release.Decision);
        Assert.Equal(OrderHoldReleaseDecision.Release, missingIntent.Decision);
        Assert.Equal(OrderHoldReleaseDecision.AlreadySettled, settled.Decision);
        Assert.Equal(OrderHoldReleaseDecision.FinishExpire, finish.Decision);
    }

    [Fact]
    public void Refuses_a_paid_order_and_a_session_that_is_not_an_expired_test_failure()
    {
        Assert.Equal(OrderStockRules.PaidReleaseMessage, Reason(Ready() with
        {
            OrderStatus = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Succeeded,
            ReservationStatus = StockReservationStatus.Confirmed
        }));
        Assert.Equal(OrderHoldReleaseRules.PaidIntentMessage, Reason(Ready(paymentStatus: "paid", intentStatus: "succeeded")));
        Assert.Equal(OrderHoldReleaseRules.PaidIntentMessage, Reason(Ready(intentStatus: "succeeded")));
        Assert.Equal(OrderHoldReleaseRules.UnpaidExpiredMessage, Reason(Ready(status: "open")));
        Assert.Equal(OrderHoldReleaseRules.UnpaidExpiredMessage, Reason(Ready(status: "complete")));
        Assert.Equal(StripeCheckoutGuard.TestKeyMessage, Reason(Ready(live: true)));
        Assert.Equal(StripeCheckoutGuard.TestKeyMessage, Reason(Ready(intentId: "pi_live_3MtwBwLkdIwHu7ix28a3tqPa")));
        Assert.Equal(StripeWebhookRules.AmountMessage, Reason(Ready(amount: 100)));
        Assert.Equal(StripeWebhookRules.CurrencyMessage, Reason(Ready(currency: "usd")));
        Assert.Equal(StripeWebhookRules.ReferenceMessage, Reason(Ready(reference: Guid.NewGuid().ToString("D"))));
        Assert.Equal(OrderHoldReleaseRules.SessionMessage, Reason(Ready(sessionId: "cs_test_other")));
        Assert.Equal(OrderHoldReleaseRules.ActiveMessage, Reason(Ready() with { Now = ExpiresAt.AddMinutes(-1) }));
        Assert.Equal(StripeWebhookRules.ClosedOrderMessage, Reason(Ready() with
        {
            ReservationStatus = StockReservationStatus.Confirming
        }));
    }

    private static string? Reason(OrderHoldReleaseContext context)
    {
        return OrderHoldReleaseRules.Decide(context).Reason;
    }

    private static OrderHoldReleaseContext Ready(
        string sessionId = "cs_test_a11YYufWQzNY63zp",
        string status = "expired",
        string paymentStatus = "unpaid",
        string? intentId = null,
        string? intentStatus = null,
        long amount = 1700,
        string currency = "eur",
        bool live = false,
        string? reference = null)
    {
        return new OrderHoldReleaseContext(
            OrderId,
            "cs_test_a11YYufWQzNY63zp",
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
                reference ?? OrderId.ToString("D"),
                OrderId.ToString("D")));
    }
}
