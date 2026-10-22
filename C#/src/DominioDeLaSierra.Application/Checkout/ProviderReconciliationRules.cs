using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public sealed record ProviderReconciliationBatchResult(
    int Processed,
    int Confirmed,
    int Released,
    int Kept,
    int Incidents,
    int Failed);

public sealed record ProviderReconciliationRow(
    Guid OrderId,
    string OrderNumber,
    string Provider,
    string ExternalReference,
    long AmountCents,
    string Currency,
    DateTimeOffset ReservedAt,
    long AgeSeconds,
    string Evidence,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string? Attention,
    string? IncidentSeverity,
    string? IncidentReconciliation,
    bool CanRecheck);

public sealed record ProviderReconciliationActionResult(bool Accepted, string Message);

public interface IProviderPaymentReconciliation
{
    Task<ProviderReconciliationBatchResult> RunOnceAsync(CancellationToken cancellationToken = default);

    Task<ProviderReconciliationBatchResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderReconciliationRow>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<ProviderReconciliationActionResult> RecheckPayPalAsync(
        Guid orderId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// La antigüedad no libera stock. PayPal se lee; Redsys, sin consulta verificada, permanece Unknown.
/// El proceso automático nace apagado.
/// </summary>
public static class ProviderReconciliationRules
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SecondDelay = TimeSpan.FromHours(1);
    public static readonly TimeSpan ThirdDelay = TimeSpan.FromHours(4);
    public static readonly TimeSpan FourthDelay = TimeSpan.FromHours(12);
    public static readonly TimeSpan LaterDelay = TimeSpan.FromHours(24);

    public const string UnavailableEvidence = "Unavailable";
    public const string RedsysSilentMessage = "Redsys no ofrece consulta de estado. La operación queda pendiente de revisión manual.";
    public const string KeptMessage = "La operación sigue abierta. La reserva se conserva.";
    public const string ConfirmedMessage = "PayPal ha confirmado el cobro y el stock se ha asentado.";
    public const string ReleasedMessage = "PayPal ha cerrado la operación sin cobro. La reserva se ha liberado.";
    public const string UnreachableMessage = "PayPal no ha respondido. La reserva se conserva.";
    public const string NotPayPalMessage = "Este pedido no tiene un pago de PayPal pendiente.";
    public const string NotConfiguredMessage = "PayPal no está configurado. La reserva se conserva.";
    public const string PayPalEventType = "paypal.reconciliation";
    public const string RedsysEventType = "redsys.reconciliation";
    public const string AdminEventType = "admin.paypal.recheck";

    public static bool MayReleaseBecauseOfAge(TimeSpan age)
    {
        _ = age;
        return false;
    }

    public static bool SupportsQuery(PaymentProvider provider)
    {
        return provider == PaymentProvider.PayPal;
    }

    public static DateTimeOffset NextAttemptAt(int completedAttempts, DateTimeOffset now)
    {
        if (completedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        var delay = completedAttempts switch
        {
            1 => FirstDelay,
            2 => SecondDelay,
            3 => ThirdDelay,
            4 => FourthDelay,
            _ => LaterDelay
        };
        return now.Add(delay);
    }

    public static bool IsDue(DateTimeOffset? nextAttemptAt, DateTimeOffset now)
    {
        return nextAttemptAt is null || now >= nextAttemptAt;
    }

    public static string EvidenceOf(PaymentEvidence evidence)
    {
        return evidence switch
        {
            PaymentEvidence.StillPayable => nameof(PaymentEvidence.StillPayable),
            PaymentEvidence.Paid => nameof(PaymentEvidence.Paid),
            PaymentEvidence.ClosedUnpaid => nameof(PaymentEvidence.ClosedUnpaid),
            _ => nameof(PaymentEvidence.Unknown)
        };
    }

    public static string PayPalAttemptEventId(Guid paymentId, int attempt)
    {
        return $"paypal-reconcile:{paymentId:N}:{attempt}";
    }

    public static string RedsysAttemptEventId(Guid paymentId, int attempt)
    {
        return $"redsys-reconcile:{paymentId:N}:{attempt}";
    }

    public static string AdminEventId(Guid paymentId, DateTimeOffset now)
    {
        return $"admin-paypal-recheck:{paymentId:N}:{now.UtcTicks}";
    }
}
