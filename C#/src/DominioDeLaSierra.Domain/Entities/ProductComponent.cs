namespace DominioDeLaSierra.Domain.Entities;

public sealed class ProductComponent
{
    public Guid Id { get; private set; }
    public Guid PackProductId { get; private set; }
    public Guid ComponentProductId { get; private set; }
    public int Quantity { get; private set; }

    public Product PackProduct { get; private set; } = null!;
    public Product ComponentProduct { get; private set; } = null!;

    private ProductComponent()
    {
    }

    public ProductComponent(Guid id, Guid packProductId, Guid componentProductId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad debe ser mayor que cero.");
        }

        Id = id;
        PackProductId = packProductId;
        ComponentProductId = componentProductId;
        Quantity = quantity;
    }
}