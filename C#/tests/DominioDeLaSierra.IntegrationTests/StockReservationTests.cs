using System.Text.Json;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class StockReservationTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reserves_a_wine_and_records_a_single_sale()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RES-WINE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var orderId = await SaveOrderAsync(Wine(wine.Id, "RES-WINE", 2));

        await ReserveAsync(orderId);

        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);
        var sale = Assert.Single(await SalesAsync(harness, wine.Id));
        Assert.Equal(-2, sale.Quantity);
        Assert.Equal(OrderStockRules.SaleNote, sale.Note);
    }

    [Fact]
    public async Task Reserves_a_standard_product()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var product = await harness.CreateProductAsync(categoryId, "RES-STD", initialStock: 4);
        var orderId = await SaveOrderAsync(new OrderLine(product.Id, "RES-STD", "RES-STD", ProductKind.Standard, 1, 1_000, 21m, null));

        await ReserveAsync(orderId);

        Assert.Equal(3, (await harness.StockAsync(product.Id))!.Quantity);
    }

    [Fact]
    public async Task Reserves_a_pack_from_its_component_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PACK-WINE", ProductKind.Wine, initialStock: 10, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "PACK-BOX", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        var orderId = await SaveOrderAsync(Pack(pack.Id, "PACK-BOX", 2, wine.Id, "PACK-WINE", 2));

        await ReserveAsync(orderId);

        Assert.Equal(6, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(-4, Assert.Single(await SalesAsync(harness, wine.Id)).Quantity);
        Assert.Null(await harness.StockAsync(pack.Id));
    }

    [Fact]
    public async Task Rejects_a_pack_with_an_inactive_component_without_changing_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "OFF-WINE", ProductKind.Wine, initialStock: 8, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "OFF-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 1));
        await harness.UpdateAsync(wine.Id, draft => draft.Active = false);
        var orderId = await SaveOrderAsync(Pack(pack.Id, "OFF-PACK", 1, wine.Id, "OFF-WINE", 1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(orderId));

        Assert.Equal(CatalogComposition.UnavailableComponentMessage, exception.Message);
        Assert.Equal(8, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Empty(await SalesAsync(harness, wine.Id));
    }

    [Fact]
    public async Task Rejects_a_pack_when_a_component_is_out_of_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "EMPTY-WINE", ProductKind.Wine, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "EMPTY-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 1));
        var orderId = await SaveOrderAsync(Pack(pack.Id, "EMPTY-PACK", 1, wine.Id, "EMPTY-WINE", 1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(orderId));

        Assert.Equal(OrderStockRules.InsufficientStockMessage, exception.Message);
        Assert.Equal(0, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Empty(await SalesAsync(harness, wine.Id));
    }

    [Fact]
    public async Task Rejects_an_inconsistent_pack_composition()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "MIX-WINE", ProductKind.Wine, initialStock: 6, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "MIX-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 1));
        var orderId = await SaveOrderAsync(Pack(pack.Id, "MIX-PACK", 1, wine.Id, "MIX-WINE", 2));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(orderId));

        Assert.Equal(CatalogComposition.InvalidCompositionMessage, exception.Message);
        Assert.Equal(6, (await harness.StockAsync(wine.Id))!.Quantity);
    }

    [Fact]
    public async Task Sums_two_packs_that_share_a_wine()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "SHARE-WINE", ProductKind.Wine, initialStock: 10, vintage: "2024");
        var first = await harness.CreateProductAsync(categoryId, "SHARE-A", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        var second = await harness.CreateProductAsync(categoryId, "SHARE-B", ProductKind.Pack, componentsJson: Components(wine.Id, 3));
        var orderId = await SaveOrderAsync(
            Pack(first.Id, "SHARE-A", 1, wine.Id, "SHARE-WINE", 2),
            Pack(second.Id, "SHARE-B", 1, wine.Id, "SHARE-WINE", 3));

        await ReserveAsync(orderId);

        Assert.Equal(5, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(-5, Assert.Single(await SalesAsync(harness, wine.Id)).Quantity);
    }

    [Fact]
    public async Task Sums_an_individual_wine_with_a_pack_that_contains_it()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "BOTH-WINE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "BOTH-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        var orderId = await SaveOrderAsync(
            Wine(wine.Id, "BOTH-WINE", 1),
            Pack(pack.Id, "BOTH-PACK", 1, wine.Id, "BOTH-WINE", 2));

        await ReserveAsync(orderId);

        Assert.Equal(2, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(-3, Assert.Single(await SalesAsync(harness, wine.Id)).Quantity);
    }

    [Fact]
    public async Task Does_not_deduct_any_unit_when_one_product_is_short()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var available = await harness.CreateProductAsync(categoryId, "PART-OK", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var missing = await harness.CreateProductAsync(categoryId, "PART-NO", ProductKind.Wine, vintage: "2023");
        var orderId = await SaveOrderAsync(Wine(available.Id, "PART-OK", 1), Wine(missing.Id, "PART-NO", 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(orderId));

        Assert.Equal(4, (await harness.StockAsync(available.Id))!.Quantity);
        Assert.Equal(0, (await harness.StockAsync(missing.Id))!.Quantity);
        Assert.Empty(await SalesAsync(harness, available.Id));
        Assert.Empty(await SalesAsync(harness, missing.Id));
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_reservations_takes_the_last_unit()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RACE-WINE", ProductKind.Wine, initialStock: 1, vintage: "2024");
        var first = await SaveOrderAsync(Wine(wine.Id, "RACE-WINE", 1));
        var second = await SaveOrderAsync(Wine(wine.Id, "RACE-WINE", 1));
        var barrier = new Barrier(2);

        var results = await Task.WhenAll(AttemptAsync(first, barrier), AttemptAsync(second, barrier));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result?.Message == OrderStockRules.InsufficientStockMessage);
        Assert.Equal(0, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(-1, Assert.Single(await SalesAsync(harness, wine.Id)).Quantity);
    }

    [Fact]
    public async Task Does_not_apply_the_same_reservation_twice()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ONCE-WINE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var orderId = await SaveOrderAsync(Wine(wine.Id, "ONCE-WINE", 2));

        await ReserveAsync(orderId);
        await ReserveAsync(orderId);

        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Single(await SalesAsync(harness, wine.Id));
    }

    [Fact]
    public async Task Releases_once_and_ignores_a_repeated_release()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "FREE-WINE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var orderId = await SaveOrderAsync(Wine(wine.Id, "FREE-WINE", 2));
        await ReserveAsync(orderId);

        await ReleaseAsync(orderId);
        await ReleaseAsync(orderId);
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => ReserveAsync(orderId));

        Assert.Equal(OrderStockRules.ReservationClosedMessage, repeated.Message);
        Assert.Equal(5, (await harness.StockAsync(wine.Id))!.Quantity);
        var cancellation = Assert.Single(await harness.MovementsAsync(wine.Id), item => item.Type == StockMovementType.Cancellation);
        Assert.Equal(2, cancellation.Quantity);
        Assert.Equal(OrderStockRules.ReleaseNote, cancellation.Note);
    }

    [Fact]
    public async Task Returns_the_snapshotted_pack_units_after_the_catalog_changes()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "SNAP-WINE", ProductKind.Wine, initialStock: 10, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "SNAP-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        var orderId = await SaveOrderAsync(Pack(pack.Id, "SNAP-PACK", 1, wine.Id, "SNAP-WINE", 2));
        await ReserveAsync(orderId);
        await harness.UpdateAsync(pack.Id, draft => draft.ComponentsJson = Components(wine.Id, 1));

        await ReleaseAsync(orderId);

        Assert.Equal(10, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(2, Assert.Single(await harness.MovementsAsync(wine.Id), item => item.Type == StockMovementType.Cancellation).Quantity);
    }

    [Fact]
    public async Task Does_not_release_a_paid_order()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAID-WINE", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var orderId = await SaveOrderAsync(Wine(wine.Id, "PAID-WINE", 1));
        await ReserveAsync(orderId);
        await MarkPaidAsync(orderId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReleaseAsync(orderId));

        Assert.Equal(OrderStockRules.PaidReleaseMessage, exception.Message);
        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.DoesNotContain(await harness.MovementsAsync(wine.Id), item => item.Type == StockMovementType.Cancellation);
    }

    [Fact]
    public async Task Does_not_release_while_payment_confirmation_is_in_progress()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "HOLD-WINE", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var orderId = await SaveOrderAsync(Wine(wine.Id, "HOLD-WINE", 1));
        await ReserveAsync(orderId);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOrderStock>().BeginPaymentConfirmationAsync(orderId, At);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReleaseAsync(orderId));

        Assert.Equal(OrderStockRules.ConfirmingReleaseMessage, exception.Message);
        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);
    }

    private async Task<Exception?> AttemptAsync(Guid orderId, Barrier barrier)
    {
        try
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await ReserveAsync(orderId);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task ReserveAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrderStock>().ReserveAsync(orderId, At);
    }

    private async Task ReleaseAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrderStock>().ReleaseAsync(orderId, At.AddMinutes(5));
    }

    private async Task MarkPaidAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        order.AddPayment(Guid.NewGuid(), At);
        order.MarkPaid(order.Payments.Single().Id, At.AddMinutes(1));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SaveOrderAsync(params OrderLine[] lines)
    {
        var order = Order.CreatePending(
            Guid.NewGuid(),
            $"DS-{Guid.NewGuid():N}"[..20],
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            lines,
            At);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<List<StockMovement>> SalesAsync(CatalogHarness harness, Guid productId)
    {
        return (await harness.MovementsAsync(productId)).Where(item => item.Type == StockMovementType.Sale).ToList();
    }

    private static string Components(Guid productId, int quantity)
    {
        return JsonSerializer.Serialize(new[] { new { productId, quantity } });
    }

    private static OrderLine Wine(Guid productId, string reference, int quantity)
    {
        return new OrderLine(productId, reference, reference, ProductKind.Wine, quantity, 1_890, 21m, null);
    }

    private static OrderLine Pack(Guid packId, string reference, int quantity, Guid componentId, string componentReference, int quantityPerPack)
    {
        return new OrderLine(
            packId,
            reference,
            reference,
            ProductKind.Pack,
            quantity,
            5_400,
            21m,
            [new OrderLineComponent(componentId, componentReference, componentReference, quantityPerPack)]);
    }
}
