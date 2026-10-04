using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Inventory;

internal static class InventoryStock
{
    public const string MissingWarehouseMessage = "El almacén principal no está disponible.";
    public const string PackStockMessage = "Un pack no tiene stock propio. Su disponibilidad depende de los productos que lo componen.";

    public static async Task<Guid?> FindActiveDefaultWarehouseIdAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Warehouses
            .Where(warehouse => warehouse.IsDefault && warehouse.Active)
            .Select(warehouse => (Guid?)warehouse.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static void EnsureQuantity(int quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentException("El stock inicial indicado no es válido.");
        }

        if (quantity > InventoryLimits.MaxStockQuantity)
        {
            throw new ArgumentException($"El stock inicial no puede superar {InventoryLimits.MaxStockQuantity} unidades.");
        }
    }

    public static void EnsurePackHasNoManualStock(ProductKind kind, int quantity)
    {
        if (kind == ProductKind.Pack && quantity > 0)
        {
            throw new ArgumentException(PackStockMessage);
        }
    }

    public static void AddOpeningBalance(
        ApplicationDbContext dbContext,
        Guid warehouseId,
        Guid productId,
        ProductKind kind,
        int quantity,
        DateTimeOffset now)
    {
        EnsureQuantity(quantity);
        EnsurePackHasNoManualStock(kind, quantity);
        if (kind is not (ProductKind.Standard or ProductKind.Wine))
        {
            return;
        }

        var stock = new Stock(Guid.NewGuid(), warehouseId, productId, quantity, now);
        dbContext.Stocks.Add(stock);
        if (quantity == 0)
        {
            return;
        }

        dbContext.StockMovements.Add(new StockMovement(
            Guid.NewGuid(),
            stock.Id,
            StockMovementType.InitialStock,
            quantity,
            now,
            InventoryLimits.InitialStockNote));
    }
}
