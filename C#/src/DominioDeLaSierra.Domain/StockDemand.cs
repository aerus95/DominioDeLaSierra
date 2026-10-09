using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.Domain;

public static class StockDemand
{
    public const string InvalidQuantityMessage = "La cantidad no es válida.";

    public static IReadOnlyList<(Guid ProductId, int Quantity)> Collect(IReadOnlyList<OrderItem> items)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("El pedido debe incluir al menos un producto.");
        }

        var totals = new Dictionary<Guid, int>();
        foreach (var item in items)
        {
            if (item.Kind == ProductKind.Pack)
            {
                if (item.Components.Count == 0)
                {
                    throw new ArgumentException("Un pack debe incluir al menos un componente.");
                }

                foreach (var component in item.Components)
                {
                    Add(totals, component.ComponentProductId, Multiply(item.Quantity, component.QuantityPerPack));
                }
            }
            else if (item.Kind is ProductKind.Wine or ProductKind.Standard)
            {
                if (item.Components.Count > 0)
                {
                    throw new ArgumentException("La composición del pack no es válida.");
                }

                Add(totals, item.ProductId, item.Quantity);
            }
            else
            {
                throw new ArgumentException("El tipo de producto no es válido.");
            }
        }

        return totals
            .OrderBy(pair => pair.Key)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
    }

    private static void Add(Dictionary<Guid, int> totals, Guid productId, int quantity)
    {
        if (productId == Guid.Empty || quantity < 1)
        {
            throw new ArgumentException(InvalidQuantityMessage);
        }

        var current = totals.GetValueOrDefault(productId);
        try
        {
            var sum = checked(current + quantity);
            if (sum > OrderAmounts.MaxLineQuantity)
            {
                throw new ArgumentException(InvalidQuantityMessage);
            }

            totals[productId] = sum;
        }
        catch (OverflowException)
        {
            throw new ArgumentException(InvalidQuantityMessage);
        }
    }

    private static int Multiply(int quantity, int quantityPerPack)
    {
        try
        {
            var units = checked(quantity * quantityPerPack);
            if (units < 1 || units > OrderAmounts.MaxLineQuantity)
            {
                throw new ArgumentException(InvalidQuantityMessage);
            }

            return units;
        }
        catch (OverflowException)
        {
            throw new ArgumentException(InvalidQuantityMessage);
        }
    }
}
