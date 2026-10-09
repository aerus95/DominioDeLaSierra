using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class StockReservation
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public StockReservationStatus Status { get; private set; }
    public DateTimeOffset ReservedAt { get; private set; }
    public DateTimeOffset? ConfirmationStartedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    public Order Order { get; private set; } = null!;

    private StockReservation()
    {
    }

    public static StockReservation Hold(Guid id, Guid orderId, DateTimeOffset reservedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("La reserva es obligatoria.", nameof(id));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("El pedido es obligatorio.", nameof(orderId));
        }

        return new StockReservation
        {
            Id = id,
            OrderId = orderId,
            Status = StockReservationStatus.Reserved,
            ReservedAt = reservedAt
        };
    }

    public void BeginConfirmation(DateTimeOffset at)
    {
        if (Status == StockReservationStatus.Confirming)
        {
            return;
        }

        if (Status != StockReservationStatus.Reserved)
        {
            throw new InvalidOperationException("La reserva no admite la confirmación del pago.");
        }

        Status = StockReservationStatus.Confirming;
        ConfirmationStartedAt = at;
    }

    public void Confirm(DateTimeOffset at)
    {
        if (Status == StockReservationStatus.Confirmed)
        {
            return;
        }

        if (Status != StockReservationStatus.Confirming)
        {
            throw new InvalidOperationException("La reserva no admite la confirmación del pago.");
        }

        Status = StockReservationStatus.Confirmed;
        ConfirmedAt = at;
    }

    public void Release(DateTimeOffset at)
    {
        if (Status != StockReservationStatus.Reserved)
        {
            throw new InvalidOperationException("La reserva ya no se puede liberar.");
        }

        Status = StockReservationStatus.Released;
        ReleasedAt = at;
    }
}
