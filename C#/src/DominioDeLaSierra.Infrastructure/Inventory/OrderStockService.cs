using System.Data;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Inventory;

public sealed class OrderStockService(ApplicationDbContext dbContext) : IOrderStock
{
    public async Task ReserveAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await ReserveWithinTransactionAsync(orderId, at, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ReserveWithinTransactionAsync(orderId, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReserveWithinTransactionAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        }

        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (reservation?.Status == StockReservationStatus.Reserved)
        {
            return;
        }

        if (reservation is not null)
        {
            throw new InvalidOperationException(OrderStockRules.ReservationClosedMessage);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException(OrderStockRules.NotPendingMessage);
        }

        if (at >= order.ReservationExpiresAt)
        {
            throw new InvalidOperationException(OrderStockRules.ReservationExpiredMessage);
        }

        var demand = StockDemand.Collect(order.Items);
        await EnsureCatalogMatchesSnapshotAsync(order, cancellationToken);
        var warehouseId = await RequireWarehouseAsync(cancellationToken);
        var stocks = await LockStocksAsync(warehouseId, demand, cancellationToken);
        foreach (var (productId, quantity) in demand)
        {
            if (stocks[productId].Quantity < quantity)
            {
                throw new InvalidOperationException(OrderStockRules.InsufficientStockMessage);
            }
        }

        foreach (var (productId, quantity) in demand)
        {
            Apply(stocks[productId], -quantity, StockMovementType.Sale, OrderStockRules.SaleNote, at);
        }

        dbContext.StockReservations.Add(StockReservation.Hold(Guid.NewGuid(), order.Id, at));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await ReleaseWithinTransactionAsync(orderId, at, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ReleaseWithinTransactionAsync(orderId, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReleaseWithinTransactionAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (reservation is null)
        {
            throw new InvalidOperationException(OrderStockRules.MissingReservationMessage);
        }

        if (order.Status == OrderStatus.Paid || reservation.Status == StockReservationStatus.Confirmed)
        {
            throw new InvalidOperationException(OrderStockRules.PaidReleaseMessage);
        }

        if (reservation.Status == StockReservationStatus.Confirming)
        {
            throw new InvalidOperationException(OrderStockRules.ConfirmingReleaseMessage);
        }

        if (reservation.Status == StockReservationStatus.Released)
        {
            return;
        }

        var demand = StockDemand.Collect(order.Items);
        var warehouseId = await RequireWarehouseAsync(cancellationToken);
        var stocks = await LockStocksAsync(warehouseId, demand, cancellationToken);
        foreach (var (productId, quantity) in demand)
        {
            Apply(stocks[productId], quantity, StockMovementType.Cancellation, OrderStockRules.ReleaseNote, at);
        }

        reservation.Release(at);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginPaymentConfirmationAsync(
        Guid orderId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await BeginPaymentConfirmationWithinAsync(orderId, at, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await BeginPaymentConfirmationWithinAsync(orderId, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ConfirmPaymentAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await ConfirmPaymentWithinAsync(orderId, at, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ConfirmPaymentWithinAsync(orderId, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task BeginPaymentConfirmationWithinAsync(
        Guid orderId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        }

        var order = await dbContext.Orders.SingleAsync(item => item.Id == orderId, cancellationToken);
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException(OrderStockRules.NotPendingMessage);
        }

        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException(OrderStockRules.MissingReservationMessage);
        reservation.BeginConfirmation(at);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ConfirmPaymentWithinAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        }

        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException(OrderStockRules.MissingReservationMessage);
        reservation.Confirm(at);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCatalogMatchesSnapshotAsync(Order order, CancellationToken cancellationToken)
    {
        var productIds = order.Items
            .Select(item => item.ProductId)
            .Concat(order.Items.SelectMany(item => item.Components).Select(component => component.ComponentProductId))
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (!await LockProductsAsync(productIds, cancellationToken))
        {
            throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
        }

        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new CatalogProduct(product.Id, product.Reference, product.Kind, product.Active))
            .ToListAsync(cancellationToken);
        var byId = products.ToDictionary(product => product.Id);
        if (byId.Count != productIds.Length)
        {
            throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
        }

        var packIds = order.Items.Where(item => item.Kind == ProductKind.Pack).Select(item => item.ProductId).ToArray();
        var liveComponents = await dbContext.ProductComponents
            .AsNoTracking()
            .Where(component => packIds.Contains(component.PackProductId))
            .Select(component => new { component.PackProductId, component.ComponentProductId, component.Quantity })
            .ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var product = byId[item.ProductId];
            if (!product.Active || product.Kind != item.Kind)
            {
                throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
            }

            if (item.Kind != ProductKind.Pack)
            {
                continue;
            }

            var snapshot = item.Components
                .OrderBy(component => component.ComponentProductId)
                .Select(component => (component.ComponentProductId, component.QuantityPerPack))
                .ToArray();
            var live = liveComponents
                .Where(component => component.PackProductId == item.ProductId)
                .OrderBy(component => component.ComponentProductId)
                .Select(component => (component.ComponentProductId, component.Quantity))
                .ToArray();
            if (!snapshot.SequenceEqual(live))
            {
                throw new ArgumentException(CatalogComposition.InvalidCompositionMessage);
            }

            foreach (var component in item.Components)
            {
                var componentProduct = byId[component.ComponentProductId];
                if (!CatalogComposition.IsEligibleComponent(componentProduct.Kind))
                {
                    throw new ArgumentException(CatalogComposition.PackComponentMessage(componentProduct.Reference));
                }

                if (!componentProduct.Active)
                {
                    throw new InvalidOperationException(CatalogComposition.UnavailableComponentMessage);
                }
            }
        }
    }

    private async Task<Guid> RequireWarehouseAsync(CancellationToken cancellationToken)
    {
        return await InventoryStock.FindActiveDefaultWarehouseIdAsync(dbContext, cancellationToken)
            ?? throw new InvalidOperationException(InventoryStock.MissingWarehouseMessage);
    }

    private async Task<Dictionary<Guid, Stock>> LockStocksAsync(
        Guid warehouseId,
        IReadOnlyList<(Guid ProductId, int Quantity)> demand,
        CancellationToken cancellationToken)
    {
        foreach (var (productId, _) in demand)
        {
            if (!await LockRowAsync(
                """SELECT "Id" FROM "Stocks" WHERE "ProductId" = @productId AND "WarehouseId" = @warehouseId FOR UPDATE""",
                ("productId", productId),
                ("warehouseId", warehouseId),
                cancellationToken))
            {
                throw new InvalidOperationException(CatalogRevision.MissingStockMessage);
            }
        }

        var ids = demand.Select(item => item.ProductId).ToArray();
        var stocks = await dbContext.Stocks
            .Where(stock => stock.WarehouseId == warehouseId && ids.Contains(stock.ProductId))
            .ToListAsync(cancellationToken);
        if (stocks.Count != ids.Length)
        {
            throw new InvalidOperationException(CatalogRevision.MissingStockMessage);
        }

        return stocks.ToDictionary(stock => stock.ProductId);
    }

    private void Apply(Stock stock, int delta, StockMovementType type, string note, DateTimeOffset at)
    {
        int next;
        try
        {
            next = checked(stock.Quantity + delta);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(OrderStockRules.InsufficientStockMessage);
        }

        if (next < 0)
        {
            throw new InvalidOperationException(OrderStockRules.InsufficientStockMessage);
        }

        if (next > InventoryLimits.MaxStockQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), StockDemand.InvalidQuantityMessage);
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), StockDemand.InvalidQuantityMessage);
        }

        stock.SetQuantity(next, at);
        dbContext.StockMovements.Add(new StockMovement(
            Guid.NewGuid(),
            stock.Id,
            type,
            delta,
            at,
            note));
    }

    private async Task<Order> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Components)
            .SingleAsync(order => order.Id == orderId, cancellationToken);
    }

    private async Task<bool> LockOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await LockRowAsync(
            """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""",
            ("id", orderId),
            null,
            cancellationToken);
    }

    private async Task<bool> LockProductsAsync(Guid[] productIds, CancellationToken cancellationToken)
    {
        foreach (var productId in productIds)
        {
            if (!await LockRowAsync(
                """SELECT "Id" FROM "Products" WHERE "Id" = @id FOR UPDATE""",
                ("id", productId),
                null,
                cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> LockRowAsync(
        string sql,
        (string Name, Guid Value) first,
        (string Name, Guid Value)? second,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        command.CommandText = sql;
        AddParameter(command, first.Name, first.Value);
        if (second is { } extra)
        {
            AddParameter(command, extra.Name, extra.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record CatalogProduct(Guid Id, string Reference, ProductKind Kind, bool Active);
}
