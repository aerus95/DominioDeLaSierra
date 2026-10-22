using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

/// <summary>
/// Agenda persistente de una conciliación de Redsys o PayPal.
/// La referencia es el identificador opaco de la pasarela, nunca una clave ni una URL de retorno.
/// </summary>
public sealed class ProviderReconciliation
{
    public const int EvidenceMaxLength = 32;
    public const int AttentionMaxLength = 240;

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid OrderId { get; private set; }
    public PaymentProvider Provider { get; private set; }
    public string ExternalReference { get; private set; } = null!;
    public string Evidence { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? Attention { get; private set; }

    private ProviderReconciliation()
    {
    }

    public static ProviderReconciliation Start(
        Guid id,
        Guid paymentId,
        Guid orderId,
        PaymentProvider provider,
        string externalReference,
        string evidence,
        DateTimeOffset attemptedAt,
        DateTimeOffset nextAttemptAt,
        string? attention)
    {
        OrderText.RequireId(id, "La conciliación");
        OrderText.RequireId(paymentId, "El pago");
        OrderText.RequireId(orderId, "El pedido");
        if (provider is not (PaymentProvider.Redsys or PaymentProvider.PayPal))
        {
            throw new ArgumentException(PaymentProviderRules.UnknownMessage);
        }

        if (nextAttemptAt < attemptedAt)
        {
            throw new ArgumentException("La próxima comprobación no puede ser anterior a este intento.");
        }

        return new ProviderReconciliation
        {
            Id = id,
            PaymentId = paymentId,
            OrderId = orderId,
            Provider = provider,
            ExternalReference = RequireReference(externalReference),
            Evidence = RequireEvidence(evidence),
            AttemptCount = 1,
            LastAttemptAt = attemptedAt,
            NextAttemptAt = nextAttemptAt,
            Attention = CleanAttention(attention)
        };
    }

    public void Record(
        string evidence,
        DateTimeOffset attemptedAt,
        DateTimeOffset nextAttemptAt,
        string? externalReference,
        string? attention)
    {
        if (attemptedAt < LastAttemptAt)
        {
            throw new ArgumentException("El intento no puede ser anterior al último registrado.");
        }

        if (nextAttemptAt < attemptedAt)
        {
            throw new ArgumentException("La próxima comprobación no puede ser anterior a este intento.");
        }

        var reference = ReservationHoldIncident.OpaqueReference(externalReference);
        if (reference is not null)
        {
            ExternalReference = reference;
        }

        Evidence = RequireEvidence(evidence);
        AttemptCount++;
        LastAttemptAt = attemptedAt;
        NextAttemptAt = nextAttemptAt;
        Attention = CleanAttention(attention);
    }

    private static string RequireReference(string? value)
    {
        return ReservationHoldIncident.OpaqueReference(value)
            ?? throw new ArgumentException("La referencia externa no es válida.");
    }

    private static string RequireEvidence(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text is not (
            nameof(PaymentEvidence.Unknown)
            or nameof(PaymentEvidence.StillPayable)
            or nameof(PaymentEvidence.Paid)
            or nameof(PaymentEvidence.ClosedUnpaid)
            or "Unavailable"))
        {
            throw new ArgumentException("La evidencia de conciliación no es válida.");
        }

        return text;
    }

    private static string? CleanAttention(string? value)
    {
        return OrderText.Optional(value, AttentionMaxLength, "La nota de conciliación");
    }
}
