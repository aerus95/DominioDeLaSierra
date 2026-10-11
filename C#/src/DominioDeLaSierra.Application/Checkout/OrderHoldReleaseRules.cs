using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public enum OrderHoldReleaseResultKind
{
    Released,
    AlreadySettled,
    Refused
}

public sealed record OrderHoldReleaseResult(OrderHoldReleaseResultKind Kind, string? Reason);

public interface IOrderHoldRelease
{
    Task<OrderHoldReleaseResult> ReleaseAsync(
        Guid orderId,
        string? orderNumber,
        CancellationToken cancellationToken = default);
}

public enum OrderHoldReleaseDecision
{
    Release,
    FinishExpire,
    AlreadySettled,
    Refuse
}

public sealed record OrderHoldReleaseContext(
    Guid OrderId,
    string? StoredSessionId,
    OrderStatus OrderStatus,
    PaymentStatus? PaymentStatus,
    long OrderTotalCents,
    long? PaymentAmountCents,
    StockReservationStatus? ReservationStatus,
    DateTimeOffset ReservationExpiresAt,
    DateTimeOffset Now,
    ObservedCheckoutSession? Session);

public sealed record OrderHoldReleaseVerdict(OrderHoldReleaseDecision Decision, string? Reason)
{
    public static OrderHoldReleaseVerdict Release()
    {
        return new OrderHoldReleaseVerdict(OrderHoldReleaseDecision.Release, null);
    }

    public static OrderHoldReleaseVerdict FinishExpire()
    {
        return new OrderHoldReleaseVerdict(OrderHoldReleaseDecision.FinishExpire, null);
    }

    public static OrderHoldReleaseVerdict AlreadySettled()
    {
        return new OrderHoldReleaseVerdict(OrderHoldReleaseDecision.AlreadySettled, null);
    }

    public static OrderHoldReleaseVerdict Refuse(string reason)
    {
        return new OrderHoldReleaseVerdict(OrderHoldReleaseDecision.Refuse, reason);
    }
}

public static class OrderHoldReleaseRules
{
    public const string UnavailableMessage = "Stripe no ha confirmado la sesión guardada.";
    public const string SessionMessage = "La sesión guardada no coincide con la respuesta de Stripe.";
    public const string UnpaidExpiredMessage = "Stripe no muestra una sesión impagada y caducada.";
    public const string PaidIntentMessage = "Stripe muestra un intento de pago cobrado.";
    public const string ActiveMessage = "La reserva todavía no ha caducado.";

    public static OrderHoldReleaseVerdict Decide(OrderHoldReleaseContext context)
    {
        if (context.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(context.StoredSessionId))
        {
            return OrderHoldReleaseVerdict.Refuse(SessionMessage);
        }

        var session = context.Session;
        if (session is null)
        {
            return OrderHoldReleaseVerdict.Refuse(UnavailableMessage);
        }

        if (!string.Equals(session.SessionId, context.StoredSessionId, StringComparison.Ordinal)
            || !context.StoredSessionId.StartsWith("cs_test_", StringComparison.Ordinal)
            || StripeCheckoutGuard.IsLiveObjectId(context.StoredSessionId)
            || StripeCheckoutGuard.IsLiveObjectId(session.SessionId))
        {
            return OrderHoldReleaseVerdict.Refuse(SessionMessage);
        }

        if (session.LiveMode || StripeCheckoutGuard.IsLiveObjectId(session.PaymentIntentId))
        {
            return OrderHoldReleaseVerdict.Refuse(StripeCheckoutGuard.TestKeyMessage);
        }

        if (string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(session.PaymentIntentStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return OrderHoldReleaseVerdict.Refuse(PaidIntentMessage);
        }

        if (!string.Equals(session.Status, "expired", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase))
        {
            return OrderHoldReleaseVerdict.Refuse(UnpaidExpiredMessage);
        }

        if (context.PaymentAmountCents is null
            || session.AmountTotal != context.OrderTotalCents
            || session.AmountTotal != context.PaymentAmountCents)
        {
            return OrderHoldReleaseVerdict.Refuse(StripeWebhookRules.AmountMessage);
        }

        if (!string.Equals(session.Currency, "eur", StringComparison.OrdinalIgnoreCase))
        {
            return OrderHoldReleaseVerdict.Refuse(StripeWebhookRules.CurrencyMessage);
        }

        if (!RefersToOrder(session.ClientReferenceId, context.OrderId)
            || !RefersToOrder(session.MetadataOrderId, context.OrderId))
        {
            return OrderHoldReleaseVerdict.Refuse(StripeWebhookRules.ReferenceMessage);
        }

        if (context.OrderStatus == OrderStatus.Expired
            && context.PaymentStatus == PaymentStatus.Cancelled
            && context.ReservationStatus == StockReservationStatus.Released)
        {
            return OrderHoldReleaseVerdict.AlreadySettled();
        }

        if (context.OrderStatus == OrderStatus.Paid
            || context.ReservationStatus == StockReservationStatus.Confirmed)
        {
            return OrderHoldReleaseVerdict.Refuse(OrderStockRules.PaidReleaseMessage);
        }

        if (context.Now < context.ReservationExpiresAt)
        {
            return OrderHoldReleaseVerdict.Refuse(ActiveMessage);
        }

        if (context.ReservationStatus == StockReservationStatus.Released
            && context.OrderStatus == OrderStatus.PendingPayment
            && context.PaymentStatus == PaymentStatus.Pending)
        {
            return OrderHoldReleaseVerdict.FinishExpire();
        }

        if (context.OrderStatus == OrderStatus.PendingPayment
            && context.PaymentStatus == PaymentStatus.Pending
            && context.ReservationStatus == StockReservationStatus.Reserved)
        {
            return OrderHoldReleaseVerdict.Release();
        }

        return OrderHoldReleaseVerdict.Refuse(StripeWebhookRules.ClosedOrderMessage);
    }

    private static bool RefersToOrder(string? value, Guid orderId)
    {
        return Guid.TryParse(value, out var referenced) && referenced == orderId;
    }
}
