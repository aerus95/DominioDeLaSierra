using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class PaymentEvent
{
    public const int ExternalEventIdMaxLength = 255;
    public const int EventTypeMaxLength = 120;
    public const int AttentionReasonMaxLength = 240;

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public string ExternalEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? AttentionReason { get; private set; }

    public Payment Payment { get; private set; } = null!;

    private PaymentEvent()
    {
    }

    internal PaymentEvent(Payment payment, Guid id, string externalEventId, string eventType, DateTimeOffset receivedAt)
    {
        OrderText.RequireId(id, "El evento de pago");
        Id = id;
        Payment = payment;
        PaymentId = payment.Id;
        ExternalEventId = OrderText.RequireToken(externalEventId, ExternalEventIdMaxLength, "El identificador del evento");
        EventType = OrderText.RequireToken(eventType, EventTypeMaxLength, "El tipo de evento");
        ReceivedAt = receivedAt;
    }

    internal void MarkProcessed(DateTimeOffset at, string? attentionReason = null)
    {
        if (ProcessedAt is not null)
        {
            throw new InvalidOperationException("El evento de pago ya está procesado.");
        }

        ProcessedAt = at;
        AttentionReason = OrderText.Optional(attentionReason, AttentionReasonMaxLength, "La incidencia");
    }
}
