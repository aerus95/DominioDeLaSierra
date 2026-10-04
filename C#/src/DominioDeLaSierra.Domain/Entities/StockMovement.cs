using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class StockMovement
{
    public const int NoteMaxLength = 200;

    public Guid Id { get; private set; }
    public Guid StockId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Note { get; private set; }

    public Stock Stock { get; private set; } = null!;

    private StockMovement()
    {
    }

    public StockMovement(
        Guid id,
        Guid stockId,
        StockMovementType type,
        int quantity,
        DateTimeOffset occurredAt,
        string? note)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException("El tipo de movimiento no es válido.", nameof(type));
        }

        if (quantity == 0)
        {
            throw new ArgumentException("El movimiento de stock no es válido.", nameof(quantity));
        }

        if (note is not null && note.Length > NoteMaxLength)
        {
            throw new ArgumentException("La nota del movimiento es demasiado larga.", nameof(note));
        }

        Id = id;
        StockId = stockId;
        Type = type;
        Quantity = quantity;
        OccurredAt = occurredAt;
        Note = note;
    }
}
