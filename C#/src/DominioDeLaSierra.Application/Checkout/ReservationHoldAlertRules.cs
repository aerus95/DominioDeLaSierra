namespace DominioDeLaSierra.Application.Checkout;

public enum ReservationHoldSeverity
{
    Held,
    Critical
}

public enum ReservationHoldReconciliation
{
    Holding,
    PaidUnsettled
}

public enum ReservationHoldResolution
{
    Verified,
    Administrative
}

public readonly record struct ReservationHoldDecision(
    bool Open,
    ReservationHoldSeverity Severity,
    ReservationHoldReconciliation Reconciliation);

/// <summary>
/// La antigüedad no libera stock. A las 24 horas, por defecto, solo se abre una incidencia.
/// Un cobro confirmado que no se puede asentar es crítico en el momento, sin esperar ese plazo.
/// </summary>
public static class ReservationHoldAlertRules
{
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromHours(24);
    public const int DefaultThresholdHours = 24;
    public const int MinimumHours = 1;
    public const int MaximumHours = 24 * 30;

    public static TimeSpan Threshold(int hours)
    {
        return hours is >= MinimumHours and <= MaximumHours
            ? TimeSpan.FromHours(hours)
            : DefaultThreshold;
    }

    public static bool IsDue(DateTimeOffset reservedAt, DateTimeOffset now, TimeSpan threshold)
    {
        return now - reservedAt >= threshold;
    }

    public static bool IsCriticalAttention(string? reason)
    {
        return reason is StripeWebhookRules.StockMessage
            or StripeWebhookRules.ClosedOrderMessage
            or PayPalRules.StockMessage
            or RedsysNotificationRules.StockMessage;
    }

    public static ReservationHoldDecision Decide(TimeSpan age, TimeSpan threshold, bool paidUnsettled)
    {
        if (paidUnsettled)
        {
            return new ReservationHoldDecision(true, ReservationHoldSeverity.Critical, ReservationHoldReconciliation.PaidUnsettled);
        }

        if (age >= threshold)
        {
            return new ReservationHoldDecision(true, ReservationHoldSeverity.Held, ReservationHoldReconciliation.Holding);
        }

        return new ReservationHoldDecision(false, ReservationHoldSeverity.Held, ReservationHoldReconciliation.Holding);
    }
}

public sealed record ReservationHoldAlertResult(int Opened, int Reviewed, int Unchanged, int Resolved);

public interface IReservationHoldAlerts
{
    Task<ReservationHoldAlertResult> RunOnceAsync(CancellationToken cancellationToken = default);

    Task<ReservationHoldAlertResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> ResolveAdministrativelyAsync(Guid orderId, CancellationToken cancellationToken = default);
}
