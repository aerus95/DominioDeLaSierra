namespace DominioDeLaSierra.Domain;

public static class PackAvailability
{
    public static int SellableQuantity(IReadOnlyList<(int QuantityPerPack, int Stock)> components)
    {
        if (components is null || components.Count == 0)
        {
            throw new ArgumentException("Un pack debe incluir al menos un componente.");
        }

        var sellable = int.MaxValue;
        foreach (var component in components)
        {
            if (component.QuantityPerPack < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(components), "La cantidad del componente no es válida.");
            }

            if (component.Stock < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(components), "El stock indicado no es válido.");
            }

            sellable = Math.Min(sellable, component.Stock / component.QuantityPerPack);
        }

        return sellable;
    }
}
