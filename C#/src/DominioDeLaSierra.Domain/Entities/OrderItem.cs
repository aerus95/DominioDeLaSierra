using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class OrderItem
{
    public const int NameMaxLength = 200;
    public const int ReferenceMaxLength = 80;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public ProductKind Kind { get; private set; }
    public int Quantity { get; private set; }
    public long UnitPriceCents { get; private set; }
    public decimal VatRate { get; private set; }
    public long LineTotalCents { get; private set; }
    public long TaxableBaseCents { get; private set; }
    public long VatCents { get; private set; }

    public Order Order { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public IReadOnlyList<OrderItemComponent> Components => components;

    private readonly List<OrderItemComponent> components = [];

    private OrderItem()
    {
    }

    internal static OrderItem Create(Order order, OrderLine line)
    {
        OrderText.RequireId(line.ProductId, "El producto");
        if (!Enum.IsDefined(line.Kind))
        {
            throw new ArgumentException("El tipo de producto no es válido.");
        }

        var name = OrderText.Require(line.Name, NameMaxLength, "El nombre del producto");
        var reference = OrderText.Require(line.Reference, ReferenceMaxLength, "La referencia del producto");
        var lineTotal = OrderAmounts.Multiply(line.UnitPriceCents, line.Quantity);
        var (taxableBase, vat) = OrderAmounts.SplitGross(lineTotal, line.VatRate);
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            Order = order,
            OrderId = order.Id,
            ProductId = line.ProductId,
            Name = name,
            Reference = reference,
            Kind = line.Kind,
            Quantity = line.Quantity,
            UnitPriceCents = line.UnitPriceCents,
            VatRate = line.VatRate,
            LineTotalCents = lineTotal,
            TaxableBaseCents = taxableBase,
            VatCents = vat
        };

        var drafts = line.Components ?? [];
        if (line.Kind == ProductKind.Pack)
        {
            if (drafts.Count == 0)
            {
                throw new ArgumentException("Un pack debe incluir al menos un componente.");
            }
        }
        else if (drafts.Count > 0)
        {
            throw new ArgumentException("Solo un pack puede guardar componentes.");
        }

        var seen = new HashSet<Guid>();
        foreach (var draft in drafts)
        {
            OrderText.RequireId(draft.ComponentProductId, "El componente");
            if (draft.ComponentProductId == line.ProductId)
            {
                throw new ArgumentException("El componente es el propio pack.");
            }

            if (!seen.Add(draft.ComponentProductId))
            {
                throw new ArgumentException("Hay un componente repetido.");
            }

            item.components.Add(new OrderItemComponent(
                item,
                draft.ComponentProductId,
                OrderText.Require(draft.Name, NameMaxLength, "El nombre del componente"),
                OrderText.Require(draft.Reference, ReferenceMaxLength, "La referencia del componente"),
                draft.Quantity));
        }

        return item;
    }
}
