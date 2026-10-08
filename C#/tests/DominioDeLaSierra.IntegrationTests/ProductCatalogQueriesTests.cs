using System.Reflection;
using DominioDeLaSierra.Application.Products.GetProducts;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class ProductCatalogQueriesTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Orders_wine_then_pack_then_standard_by_price_and_reference_before_paging()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Category(Guid.NewGuid(), "Orden comercial", "orden-comercial", null, true);
        var now = DateTimeOffset.UtcNow;
        db.Categories.Add(category);
        db.Products.AddRange(
            Item(category.Id, "W-Z", "Zeta", 10m, ProductKind.Wine, now),
            Item(category.Id, "W-B", "Beta vino", 20m, ProductKind.Wine, now),
            Item(category.Id, "W-A", "Alfa vino", 20m, ProductKind.Wine, now),
            Item(category.Id, "P-B", "Beta pack", 5m, ProductKind.Pack, now),
            Item(category.Id, "P-A", "Alfa pack", 15m, ProductKind.Pack, now),
            Item(category.Id, "S-B", "Alfa standard", 1m, ProductKind.Standard, now),
            Item(category.Id, "S-A", "Zeta standard", 1m, ProductKind.Standard, now),
            Item(category.Id, "S-C", "Medio", 30m, ProductKind.Standard, now));
        await db.SaveChangesAsync();

        var queries = scope.ServiceProvider.GetRequiredService<IProductCatalogQueries>();
        var first = await queries.GetProductsAsync(GetProductsQuery.Create(page: 1, pageSize: 3));
        var second = await queries.GetProductsAsync(GetProductsQuery.Create(page: 2, pageSize: 3));

        Assert.Equal(8, first.TotalItems);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(["W-Z", "W-A", "W-B"], first.Items.Select(item => item.Reference));
        Assert.Equal(["Wine", "Wine", "Wine"], first.Items.Select(item => item.Kind));
        Assert.Equal(["P-B", "P-A", "S-A"], second.Items.Select(item => item.Reference));
        Assert.Equal(["Pack", "Pack", "Standard"], second.Items.Select(item => item.Kind));
        Assert.DoesNotContain(
            typeof(ProductListItemDto).GetProperties().Select(property => property.Name),
            name => name.Contains("Component", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Returns_pack_components_by_name_then_reference_and_hides_missing_products()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Category(Guid.NewGuid(), "Composición", "composicion", null, true);
        var hiddenCategory = new Category(Guid.NewGuid(), "Oculta", "oculta", null, false);
        var now = DateTimeOffset.UtcNow;
        const string earlyImage = "/media/products/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp";
        const string zetaImage = "/media/products/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp";
        var alfaEarly = Item(category.Id, "A-REF", "Alfa", 10m, ProductKind.Wine, now, description: "Notas tempranas", primaryImageUrl: earlyImage);
        var alfaLate = Item(category.Id, "B-REF", "Alfa", 11m, ProductKind.Wine, now, active: false, description: "Notas tardías");
        var zeta = Item(category.Id, "Z-REF", "Zeta", 12m, ProductKind.Wine, now, description: "Notas de zeta", primaryImageUrl: zetaImage);
        var pack = Item(category.Id, "DUO", "Dúo", 30m, ProductKind.Pack, now, description: "Caja de dos");
        var wine = Item(category.Id, "SOLO", "Solo", 9m, ProductKind.Wine, now);
        var inactive = Item(category.Id, "OCULTO", "Oculto", 8m, ProductKind.Pack, now, active: false);
        var hidden = Item(hiddenCategory.Id, "FUERA", "Fuera", 7m, ProductKind.Wine, now);
        db.Categories.AddRange(category, hiddenCategory);
        db.Products.AddRange(alfaEarly, alfaLate, zeta, pack, wine, inactive, hidden);
        db.ProductComponents.AddRange(
            new ProductComponent(Guid.NewGuid(), pack.Id, zeta.Id, 1),
            new ProductComponent(Guid.NewGuid(), pack.Id, alfaLate.Id, 2),
            new ProductComponent(Guid.NewGuid(), pack.Id, alfaEarly.Id, 1));
        await db.SaveChangesAsync();

        var queries = scope.ServiceProvider.GetRequiredService<IProductCatalogQueries>();
        var catalog = await queries.GetProductsAsync(GetProductsQuery.Create(pageSize: 20));
        var components = await queries.GetPackComponentsAsync(GetPackComponentsQuery.Create("duo"));

        Assert.NotNull(components);
        Assert.Equal([alfaEarly.Id, alfaLate.Id, zeta.Id], components.Select(item => item.ProductId));
        Assert.Equal(["Alfa", "Alfa", "Zeta"], components.Select(item => item.Name));
        Assert.Equal(["Notas tempranas", "Notas tardías", "Notas de zeta"], components.Select(item => item.Description));
        Assert.Equal([earlyImage, null, zetaImage], components.Select(item => item.PrimaryImageUrl));
        Assert.Equal([1, 2, 1], components.Select(item => item.Quantity));
        Assert.Equal("Pack", catalog.Items.Single(item => item.Reference == "DUO").Kind);
        Assert.DoesNotContain(catalog.Items, item => item.Description == "Notas tardías");
        var wineComponents = await queries.GetPackComponentsAsync(GetPackComponentsQuery.Create("solo"));
        Assert.NotNull(wineComponents);
        Assert.Empty(wineComponents);
        Assert.Null(await queries.GetPackComponentsAsync(GetPackComponentsQuery.Create("no-existe")));
        Assert.Null(await queries.GetPackComponentsAsync(GetPackComponentsQuery.Create("oculto")));
        Assert.Null(await queries.GetPackComponentsAsync(GetPackComponentsQuery.Create("fuera")));
    }

    [Fact]
    public async Task Projects_grape_and_alcohol_for_wines_and_nulls_otherwise()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Category(Guid.NewGuid(), "Ficha", "ficha", null, true);
        var now = DateTimeOffset.UtcNow;
        var rufete = Item(category.Id, "W-RUFETE", "Rufete", 18m, ProductKind.Wine, now);
        var empty = Item(category.Id, "W-NULL", "Sin datos", 19m, ProductKind.Wine, now);
        var withoutProfile = Item(category.Id, "W-SINFICHA", "Sin ficha", 17m, ProductKind.Wine, now);
        var standard = Item(category.Id, "S-STD", "Estándar", 8m, ProductKind.Standard, now);
        db.Categories.Add(category);
        db.Products.AddRange(rufete, empty, withoutProfile, standard);
        db.Wines.AddRange(
            new Wine(rufete.Id, null, "Rufete", 13.5m),
            new Wine(empty.Id, null, null, null));
        await db.SaveChangesAsync();

        var queries = scope.ServiceProvider.GetRequiredService<IProductCatalogQueries>();
        var listed = await queries.GetProductsAsync(GetProductsQuery.Create(pageSize: 20));
        var byReference = listed.Items.ToDictionary(item => item.Reference);

        AssertWineFacts(byReference["W-RUFETE"], "Rufete", 13.5m);
        AssertWineFacts(byReference["W-NULL"], null, null);
        AssertWineFacts(byReference["W-SINFICHA"], null, null);
        AssertWineFacts(byReference["S-STD"], null, null);

        AssertWineFacts(await RequireBySlug(queries, "w-rufete"), "Rufete", 13.5m);
        AssertWineFacts(await RequireBySlug(queries, "w-null"), null, null);
        AssertWineFacts(await RequireBySlug(queries, "w-sinficha"), null, null);
        AssertWineFacts(await RequireBySlug(queries, "s-std"), null, null);
    }

    private static async Task<ProductListItemDto> RequireBySlug(IProductCatalogQueries queries, string slug)
    {
        var item = await queries.GetProductBySlugAsync(GetProductBySlugQuery.Create(slug));
        Assert.NotNull(item);
        return item;
    }

    private static void AssertWineFacts(ProductListItemDto item, string? grape, decimal? alcoholPercent)
    {
        Assert.Equal(grape, item.Grape);
        Assert.Equal(alcoholPercent, item.AlcoholPercent);
    }

    private static Product Item(
        Guid categoryId,
        string reference,
        string name,
        decimal price,
        ProductKind kind,
        DateTimeOffset now,
        bool active = true,
        string description = "Descripción de prueba",
        string? primaryImageUrl = null)
    {
        return new Product(
            Guid.NewGuid(),
            reference,
            name,
            reference.ToLowerInvariant(),
            description,
            categoryId,
            price,
            21m,
            active,
            now,
            now,
            kind,
            primaryImageUrl);
    }
}
