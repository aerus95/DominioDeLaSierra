using System.Text.Json;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class InventoryTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Standard_with_zero_stock_has_a_stock_row_and_no_movement()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-STD-0");

        var stock = await harness.StockAsync(created.Id);
        var movements = await harness.MovementsAsync(created.Id);

        Assert.NotNull(stock);
        Assert.Equal(0, stock.Quantity);
        Assert.Empty(movements);
    }

    [Fact]
    public async Task Standard_with_opening_stock_records_the_initial_movement()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-STD-5", initialStock: 5);

        var stock = await harness.StockAsync(created.Id);
        var movement = Assert.Single(await harness.MovementsAsync(created.Id));

        Assert.Equal(5, stock!.Quantity);
        Assert.Equal(StockMovementType.InitialStock, movement.Type);
        Assert.Equal(5, movement.Quantity);
        Assert.Equal(InventoryLimits.InitialStockNote, movement.Note);
    }

    [Fact]
    public async Task Wine_with_zero_stock_exists_without_a_movement()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(
            categoryId,
            "TEST-WINE-0",
            ProductKind.Wine,
            vintage: "2024",
            grape: "Rufete");

        Assert.NotNull(await harness.WineAsync(created.Id));
        Assert.Equal(0, (await harness.StockAsync(created.Id))!.Quantity);
        Assert.Empty(await harness.MovementsAsync(created.Id));
    }

    [Fact]
    public async Task Wine_with_opening_stock_records_the_initial_movement()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(
            categoryId,
            "TEST-WINE-6",
            ProductKind.Wine,
            initialStock: 6,
            vintage: "2024");

        var movement = Assert.Single(await harness.MovementsAsync(created.Id));

        Assert.Equal(6, (await harness.StockAsync(created.Id))!.Quantity);
        Assert.Equal(StockMovementType.InitialStock, movement.Type);
        Assert.Equal(6, movement.Quantity);
    }

    [Fact]
    public async Task Pack_has_components_and_no_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var component = await harness.CreateProductAsync(categoryId, "TEST-COMP");
        var pack = await harness.CreateProductAsync(
            categoryId,
            "TEST-PACK",
            ProductKind.Pack,
            componentsJson: JsonSerializer.Serialize(new[] { new { productId = component.Id, quantity = 2 } }));

        var saved = Assert.Single(await harness.ComponentsAsync(pack.Id));

        Assert.Equal(component.Id, saved.ComponentProductId);
        Assert.Equal(2, saved.Quantity);
        Assert.Null(await harness.StockAsync(pack.Id));
        Assert.Empty(await harness.MovementsAsync(pack.Id));
    }

    [Fact]
    public async Task Decreasing_stock_writes_a_negative_adjustment()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-ADJ-DOWN", initialStock: 20);

        await harness.UpdateAsync(created.Id, draft => draft.Stock = "17");

        var adjustment = (await harness.MovementsAsync(created.Id)).Single(item => item.Type == StockMovementType.Adjustment);
        Assert.Equal(17, (await harness.StockAsync(created.Id))!.Quantity);
        Assert.Equal(-3, adjustment.Quantity);
    }

    [Fact]
    public async Task Increasing_stock_writes_a_positive_adjustment()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-ADJ-UP", initialStock: 17);

        await harness.UpdateAsync(created.Id, draft => draft.Stock = "25");

        var adjustment = (await harness.MovementsAsync(created.Id)).Single(item => item.Type == StockMovementType.Adjustment);
        Assert.Equal(25, (await harness.StockAsync(created.Id))!.Quantity);
        Assert.Equal(8, adjustment.Quantity);
    }

    [Fact]
    public async Task Keeping_the_same_stock_does_not_write_a_movement()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-ADJ-SAME", initialStock: 25);
        var before = (await harness.MovementsAsync(created.Id)).Count;

        await harness.UpdateAsync(created.Id, draft => draft.Stock = "25");

        Assert.Equal(before, (await harness.MovementsAsync(created.Id)).Count);
        Assert.Equal(25, (await harness.StockAsync(created.Id))!.Quantity);
    }

    [Fact]
    public async Task Editing_common_data_keeps_the_kind()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-EDIT", initialStock: 3);

        await harness.UpdateAsync(created.Id, draft =>
        {
            draft.Name = "Nombre editado";
            draft.Description = "Descripción editada";
        });

        var product = await harness.ProductAsync(created.Id);
        Assert.Equal(ProductKind.Standard, product.Kind);
        Assert.Equal("Nombre editado", product.Name);
        Assert.Equal("Descripción editada", product.Description);
    }

    [Fact]
    public async Task Editing_a_wine_keeps_the_kind()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var created = await harness.CreateProductAsync(categoryId, "TEST-WINE-EDIT", ProductKind.Wine, vintage: "2020", grape: "Rufete");

        await harness.UpdateAsync(created.Id, draft => draft.Grape = "Rufete seleccionada");

        var product = await harness.ProductAsync(created.Id);
        var wine = await harness.WineAsync(created.Id);
        Assert.Equal(ProductKind.Wine, product.Kind);
        Assert.Equal("Rufete seleccionada", wine!.Grape);
    }

    [Fact]
    public async Task Editing_a_pack_keeps_the_kind()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var component = await harness.CreateProductAsync(categoryId, "TEST-PACK-COMP");
        var pack = await harness.CreateProductAsync(
            categoryId,
            "TEST-PACK-EDIT",
            ProductKind.Pack,
            componentsJson: JsonSerializer.Serialize(new[] { new { productId = component.Id, quantity = 1 } }));

        await harness.UpdateAsync(pack.Id, draft => draft.Description = "Pack editado");

        var product = await harness.ProductAsync(pack.Id);
        Assert.Equal(ProductKind.Pack, product.Kind);
        Assert.Equal("Pack editado", product.Description);
        Assert.Null(await harness.StockAsync(pack.Id));
    }

    [Fact]
    public async Task An_invalid_wine_does_not_leave_a_product()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.CreateProductAsync(
            categoryId,
            "TEST-WINE-BAD",
            ProductKind.Wine,
            vintage: new string('a', 21)));

        Assert.Equal(0, await harness.CountAsync<Product>());
        Assert.Equal(0, await harness.CountAsync<Wine>());
    }

    [Fact]
    public async Task An_invalid_pack_does_not_leave_a_product()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.CreateProductAsync(
            categoryId,
            "TEST-PACK-EMPTY",
            ProductKind.Pack,
            componentsJson: "[]"));

        Assert.Equal(CatalogComposition.EmptyPackMessage, exception.Message);
        Assert.Equal(0, await harness.CountAsync<Product>());
        Assert.Equal(0, await harness.CountAsync<ProductComponent>());
    }

    [Fact]
    public async Task A_duplicate_reference_does_not_leave_orphan_rows()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        await harness.CreateProductAsync(categoryId, "TEST-DUP", initialStock: 4);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => harness.CreateProductAsync(
            categoryId,
            "TEST-DUP",
            ProductKind.Wine,
            vintage: "2024"));

        Assert.Equal(1, await harness.CountAsync<Product>());
        Assert.Equal(0, await harness.CountAsync<Wine>());
        Assert.Equal(1, await harness.CountAsync<Stock>());
        Assert.Equal(0, await harness.CountAsync<ProductComponent>());
    }
}
