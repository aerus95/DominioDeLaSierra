using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class OrderItemComponent
{
    public Guid Id { get; private set; }
    public Guid OrderItemId { get; private set; }
    public Guid ComponentProductId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public int QuantityPerPack { get; private set; }

    public OrderItem OrderItem { get; private set; } = null!;
    public Product ComponentProduct { get; private set; } = null!;

    private OrderItemComponent()
    {
    }

    internal OrderItemComponent(
        OrderItem orderItem,
        Guid componentProductId,
        string name,
        string reference,
        int quantityPerPack)
    {
        if (quantityPerPack < 1 || quantityPerPack > OrderAmounts.MaxLineQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityPerPack), "La cantidad del componente no es válida.");
        }

        Id = Guid.NewGuid();
        OrderItem = orderItem;
        OrderItemId = orderItem.Id;
        ComponentProductId = componentProductId;
        Name = name;
        Reference = reference;
        QuantityPerPack = quantityPerPack;
    }
}
