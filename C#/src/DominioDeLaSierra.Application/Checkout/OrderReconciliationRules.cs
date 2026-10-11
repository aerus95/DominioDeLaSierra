using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public enum OrderReconciliationResultKind
{
    Confirmed,
    AlreadySettled,
    Refused
}

public sealed record OrderReconciliationResult(OrderReconciliationResultKind Kind, string? Reason);

public interface IOrderPaymentReconciliation
{
    Task<OrderReconciliationResult> ReconcileAsync(
        Guid orderId,
        string? orderNumber,
        CancellationToken cancellationToken = default);
}

public enum OrderReconciliationDecision
{
    Confirm,
    FinishHold,
    AlreadySettled,
    Refuse
}

public sealed record OrderReconciliationContext(
    Guid OrderId,
    string? StoredSessionId,
    string? StoredPaymentIntentId,
    OrderStatus OrderStatus,
    PaymentStatus? PaymentStatus,
    long OrderTotalCents,
    long? PaymentAmountCents,
    StockReservationStatus? ReservationStatus,
    DateTimeOffset ReservationExpiresAt,
    DateTimeOffset Now,
    ObservedCheckoutSession? Session);

public sealed record OrderReconciliationVerdict(OrderReconciliationDecision Decision, string? Reason)
{
    public static OrderReconciliationVerdict Confirm()
    {
        return new OrderReconciliationVerdict(OrderReconciliationDecision.Confirm, null);
    }

    public static OrderReconciliationVerdict FinishHold()
    {
        return new OrderReconciliationVerdict(OrderReconciliationDecision.FinishHold, null);
    }

    public static OrderReconciliationVerdict AlreadySettled()
    {
        return new OrderReconciliationVerdict(OrderReconciliationDecision.AlreadySettled, null);
    }

    public static OrderReconciliationVerdict Refuse(string reason)
    {
        return new OrderReconciliationVerdict(OrderReconciliationDecision.Refuse, reason);
    }
}

public static class OrderReconciliationRules
{
    public const string EventType = "order.reconciliation.paid";
    public const string UnavailableMessage = "Stripe no ha confirmado la sesión guardada.";
    public const string SessionMessage = "La sesión guardada no coincide con la respuesta de Stripe.";
    public const string IncompleteMessage = "Stripe no muestra un cobro completado de pruebas.";
    public const string ReleasedMessage = "La reserva ya se liberó. No se confirma el cobro sin revisar el stock.";
    public const string HoldMessage = "El pedido pagado conserva una reserva sin cerrar. Hace falta revisión.";

    public static string EventIdFor(Guid orderId)
    {
        return $"reconcile:{orderId:N}:paid";
    }

    public static OrderReconciliationVerdict Decide(OrderReconciliationContext context)
    {
        if (context.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(context.StoredSessionId))
        {
            return OrderReconciliationVerdict.Refuse(SessionMessage);
        }

        var session = context.Session;
        if (session is null)
        {
            return OrderReconciliationVerdict.Refuse(UnavailableMessage);
        }

        if (!string.Equals(session.SessionId, context.StoredSessionId, StringComparison.Ordinal)
            || !context.StoredSessionId.StartsWith("cs_test_", StringComparison.Ordinal)
            || StripeCheckoutGuard.IsLiveObjectId(context.StoredSessionId)
            || StripeCheckoutGuard.IsLiveObjectId(session.SessionId))
        {
            return OrderReconciliationVerdict.Refuse(SessionMessage);
        }

        if (session.LiveMode || StripeCheckoutGuard.IsLiveObjectId(session.PaymentIntentId))
        {
            return OrderReconciliationVerdict.Refuse(StripeCheckoutGuard.TestKeyMessage);
        }

        if (!string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.PaymentIntentStatus, "succeeded", StringComparison.OrdinalIgnoreCase)
            || !StripeCheckoutGuard.IsTestPaymentIntent(session.PaymentIntentId))
        {
            return OrderReconciliationVerdict.Refuse(IncompleteMessage);
        }

        if (context.PaymentAmountCents is null
            || session.AmountTotal != context.OrderTotalCents
            || session.AmountTotal != context.PaymentAmountCents)
        {
            return OrderReconciliationVerdict.Refuse(StripeWebhookRules.AmountMessage);
        }

        if (!string.Equals(session.Currency, "eur", StringComparison.OrdinalIgnoreCase))
        {
            return OrderReconciliationVerdict.Refuse(StripeWebhookRules.CurrencyMessage);
        }

        if (!RefersToOrder(session.ClientReferenceId, context.OrderId)
            || !RefersToOrder(session.MetadataOrderId, context.OrderId))
        {
            return OrderReconciliationVerdict.Refuse(StripeWebhookRules.ReferenceMessage);
        }

        if (context.StoredPaymentIntentId is not null
            && !string.Equals(context.StoredPaymentIntentId, session.PaymentIntentId, StringComparison.Ordinal))
        {
            return OrderReconciliationVerdict.Refuse(StripeWebhookRules.IntentMessage);
        }

        if (context.ReservationStatus == StockReservationStatus.Released)
        {
            return OrderReconciliationVerdict.Refuse(ReleasedMessage);
        }

        var sameIntent = context.StoredPaymentIntentId is not null
            && string.Equals(context.StoredPaymentIntentId, session.PaymentIntentId, StringComparison.Ordinal);
        if (context.OrderStatus == OrderStatus.Paid
            && context.PaymentStatus == PaymentStatus.Succeeded
            && sameIntent
            && context.ReservationStatus == StockReservationStatus.Confirmed)
        {
            return OrderReconciliationVerdict.AlreadySettled();
        }

        if (context.OrderStatus == OrderStatus.Paid
            && context.PaymentStatus == PaymentStatus.Succeeded
            && sameIntent
            && context.ReservationStatus == StockReservationStatus.Confirming)
        {
            return OrderReconciliationVerdict.FinishHold();
        }

        if (context.OrderStatus == OrderStatus.Paid
            && context.PaymentStatus == PaymentStatus.Succeeded
            && sameIntent
            && context.ReservationStatus == StockReservationStatus.Reserved)
        {
            return OrderReconciliationVerdict.Refuse(HoldMessage);
        }

        if (context.OrderStatus == OrderStatus.PendingPayment
            && context.PaymentStatus == PaymentStatus.Pending
            && context.ReservationStatus is StockReservationStatus.Reserved
                or StockReservationStatus.Confirming
                or StockReservationStatus.Confirmed)
        {
            // La caducidad no impide la confirmación: la unidad sigue retenida y Stripe ya cobró.
            return OrderReconciliationVerdict.Confirm();
        }

        return OrderReconciliationVerdict.Refuse(StripeWebhookRules.ClosedOrderMessage);
    }

    private static bool RefersToOrder(string? value, Guid orderId)
    {
        return Guid.TryParse(value, out var referenced) && referenced == orderId;
    }
}
