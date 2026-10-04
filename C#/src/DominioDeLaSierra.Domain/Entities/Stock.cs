namespace DominioDeLaSierra.Domain.Entities;

public sealed class Stock
{
    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public ICollection<StockMovement> Movements { get; private set; } = new List<StockMovement>();

    private Stock()
    {
    }

    public Stock(Guid id, Guid warehouseId, Guid productId, int quantity, DateTimeOffset updatedAt)
    {
        if (quantity < 0)
        {
            throw new ArgumentException("El stock inicial indicado no es válido.", nameof(quantity));
        }

        Id = id;
        WarehouseId = warehouseId;
        ProductId = productId;
        Quantity = quantity;
        UpdatedAt = updatedAt;
    }

    public void SetQuantity(int quantity, DateTimeOffset updatedAt)
    {
        if (quantity < 0)
        {
            throw new ArgumentException("El stock indicado no es válido.", nameof(quantity));
        }

        Quantity = quantity;
        UpdatedAt = updatedAt;
    }
}
