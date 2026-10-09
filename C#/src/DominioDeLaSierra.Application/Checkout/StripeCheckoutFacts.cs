namespace DominioDeLaSierra.Application.Checkout;

public enum StripeCheckoutFact
{
    Unavailable,
    NotFound,
    Open,
    Paid,
    Pending,
    ExpiredUnpaid,
    Contradiction
}

public sealed record ObservedCheckoutSession(
    string? SessionId,
    string? Status,
    string? PaymentStatus,
    string? PaymentIntentId,
    string? PaymentIntentStatus,
    long? AmountTotal,
    string? Currency,
    bool LiveMode,
    string? ClientReferenceId,
    string? MetadataOrderId);

public sealed record StripeSessionQuery(Guid OrderId, string? SessionId);

public sealed record StripeSessionInspection(
    StripeCheckoutFact Fact,
    string? SessionId,
    string? PaymentIntentId,
    long? AmountTotal,
    string? Currency,
    string? ClientReferenceId,
    string? MetadataOrderId,
    string? AttentionReason)
{
    public static StripeSessionInspection Unavailable()
    {
        return new StripeSessionInspection(StripeCheckoutFact.Unavailable, null, null, null, null, null, null, null);
    }

    public static StripeSessionInspection NotFound()
    {
        return new StripeSessionInspection(StripeCheckoutFact.NotFound, null, null, null, null, null, null, null);
    }
}

public static class StripeCheckoutFacts
{
    private static readonly HashSet<string> PendingIntents = new(StringComparer.Ordinal)
    {
        "processing",
        "requires_action",
        "requires_confirmation",
        "requires_capture"
    };

    public static StripeSessionInspection Combine(IReadOnlyList<ObservedCheckoutSession> sessions, Guid orderId)
    {
        if (sessions.Count == 0)
        {
            return StripeSessionInspection.NotFound();
        }

        var classified = sessions.Select(session => Classify(session, orderId)).ToArray();
        var contradiction = classified.FirstOrDefault(item => item.Fact == StripeCheckoutFact.Contradiction);
        if (contradiction is not null)
        {
            return contradiction;
        }

        var paid = classified.Where(item => item.Fact == StripeCheckoutFact.Paid).ToArray();
        if (paid.Length > 1 && paid.Select(item => item.AmountTotal).Distinct().Count() > 1)
        {
            return Copy(paid[0], StripeCheckoutFact.Contradiction, StripeWebhookRules.AmountMessage);
        }

        if (paid.Length > 0)
        {
            return paid[0];
        }

        var pending = classified.FirstOrDefault(item => item.Fact == StripeCheckoutFact.Pending);
        if (pending is not null)
        {
            return pending;
        }

        var open = classified.FirstOrDefault(item => item.Fact == StripeCheckoutFact.Open);
        if (open is not null)
        {
            return open;
        }

        if (classified.All(item => item.Fact == StripeCheckoutFact.ExpiredUnpaid))
        {
            return classified[0];
        }

        return Copy(classified[0], StripeCheckoutFact.Contradiction, ReservationSweepRules.UnresolvedMessage);
    }

    public static StripeSessionInspection Classify(ObservedCheckoutSession session, Guid orderId)
    {
        if (session.LiveMode || ContainsLive(session.SessionId) || ContainsLive(session.PaymentIntentId))
        {
            return Copy(session, StripeCheckoutFact.Contradiction, StripeCheckoutGuard.TestKeyMessage);
        }

        if (RefersToAnotherOrder(session.ClientReferenceId, orderId) || RefersToAnotherOrder(session.MetadataOrderId, orderId))
        {
            return Copy(session, StripeCheckoutFact.Contradiction, StripeWebhookRules.ReferenceMessage);
        }

        if (IsPaid(session))
        {
            return Copy(session, StripeCheckoutFact.Paid, null);
        }

        if (string.Equals(session.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            return Copy(session, StripeCheckoutFact.Open, null);
        }

        if (IsPendingIntent(session.PaymentIntentStatus))
        {
            return Copy(session, StripeCheckoutFact.Pending, null);
        }

        if (string.Equals(session.Status, "expired", StringComparison.OrdinalIgnoreCase))
        {
            return Copy(session, StripeCheckoutFact.ExpiredUnpaid, null);
        }

        if (string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase)
            && string.Equals(session.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase)
            && session.PaymentIntentStatus is "canceled" or "requires_payment_method")
        {
            return Copy(session, StripeCheckoutFact.ExpiredUnpaid, null);
        }

        return Copy(session, StripeCheckoutFact.Contradiction, ReservationSweepRules.UnresolvedMessage);
    }

    private static bool IsPaid(ObservedCheckoutSession session)
    {
        return string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(session.PaymentIntentStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingIntent(string? status)
    {
        return status is not null && PendingIntents.Contains(status);
    }

    private static bool ContainsLive(string? value)
    {
        return value is not null && value.Contains("live", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RefersToAnotherOrder(string? value, Guid orderId)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (!Guid.TryParse(value, out var referenced) || referenced != orderId);
    }

    private static StripeSessionInspection Copy(ObservedCheckoutSession session, StripeCheckoutFact fact, string? reason)
    {
        return new StripeSessionInspection(
            fact,
            session.SessionId,
            session.PaymentIntentId,
            session.AmountTotal,
            session.Currency,
            session.ClientReferenceId,
            session.MetadataOrderId,
            reason);
    }

    private static StripeSessionInspection Copy(StripeSessionInspection inspection, StripeCheckoutFact fact, string? reason)
    {
        return inspection with { Fact = fact, AttentionReason = reason };
    }
}
