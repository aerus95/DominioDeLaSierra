using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class FulfillmentTransition
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public FulfillmentStatus FromStatus { get; private set; }
    public FulfillmentStatus ToStatus { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }

    public Order Order { get; private set; } = null!;

    private FulfillmentTransition()
    {
    }

    internal FulfillmentTransition(
        Guid id,
        Order order,
        FulfillmentStatus fromStatus,
        FulfillmentStatus toStatus,
        DateTimeOffset occurredAt,
        Guid actorUserId,
        string? carrier,
        string? trackingNumber)
    {
        OrderText.RequireId(id, "El cambio de preparación");
        Id = id;
        Order = order;
        OrderId = order.Id;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        Carrier = carrier;
        TrackingNumber = trackingNumber;
    }
}
