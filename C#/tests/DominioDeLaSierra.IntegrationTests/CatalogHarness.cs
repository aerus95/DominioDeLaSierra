using System.Text.Json;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.UpdateProduct;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

internal sealed class CatalogHarness(IServiceProvider services)
{
    public async Task<Guid> CreateCategoryAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<ICreateCategory>().ExecuteAsync(
            new CreateCategoryCommand($"TEST-{Guid.NewGuid():N}"[..20], null, null));
        return created.Id;
    }

    public async Task<CreatedProductDto> CreateProductAsync(
        Guid categoryId,
        string reference,
        ProductKind kind = ProductKind.Standard,
        int initialStock = 0,
        string? componentsJson = null,
        string? vintage = null,
        string? grape = null,
        string? alcohol = null)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateProduct>().ExecuteAsync(
            new CreateProductCommand(
                reference,
                reference,
                null,
                "Descripción de prueba",
                categoryId,
                18.90m,
                21m,
                true,
                initialStock,
                kind,
                vintage,
                grape,
                alcohol,
                componentsJson));
    }

    public async Task UpdateAsync(Guid productId, Action<UpdateDraft>? change = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        var product = await db.Products.SingleAsync(item => item.Id == productId);
        var wine = await db.Wines.SingleOrDefaultAsync(item => item.ProductId == productId);
        var components = await db.ProductComponents.Where(item => item.PackProductId == productId).ToListAsync();
        var stock = await db.Stocks.SingleOrDefaultAsync(item => item.ProductId == productId);
        var draft = new UpdateDraft
        {
            Reference = product.Reference,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            Price = product.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            VatRate = product.VatRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Active = product.Active,
            Vintage = wine?.Vintage,
            Grape = wine?.Grape,
            Alcohol = wine?.AlcoholPercent?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Stock = stock?.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            ComponentsJson = JsonSerializer.Serialize(components.Select(item => new
            {
                productId = item.ComponentProductId,
                quantity = item.Quantity
            }))
        };
        change?.Invoke(draft);
        var revision = CatalogRevision.Compute(
            product.Kind,
            product.Reference,
            product.Name,
            product.Slug,
            product.Description,
            product.CategoryId,
            product.Price,
            product.VatRate,
            product.Active,
            wine?.Vintage,
            wine?.Grape,
            wine?.AlcoholPercent,
            components.Select(item => (item.ComponentProductId, item.Quantity)));
        var loadedStock = stock?.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await scope.ServiceProvider.GetRequiredService<IUpdateProduct>().ExecuteAsync(
            new UpdateProductCommand(
                productId,
                revision,
                draft.Reference,
                draft.Name,
                null,
                draft.Description,
                draft.CategoryId.ToString("D"),
                draft.Price,
                draft.VatRate,
                draft.Active,
                true,
                product.Kind == ProductKind.Pack ? null : loadedStock,
                product.Kind == ProductKind.Pack ? null : draft.Stock,
                product.Kind != ProductKind.Pack,
                draft.Vintage,
                draft.Grape,
                draft.Alcohol,
                product.Kind == ProductKind.Pack ? draft.ComponentsJson : null,
                product.Kind == ProductKind.Pack));
    }

    public async Task<Product> ProductAsync(Guid productId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        return await db.Products.AsNoTracking().SingleAsync(item => item.Id == productId);
    }

    public async Task<int> CountAsync<T>() where T : class
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        return await db.Set<T>().CountAsync();
    }

    public async Task<Stock?> StockAsync(Guid productId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        return await db.Stocks.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId);
    }

    public async Task<List<StockMovement>> MovementsAsync(Guid productId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        var stockId = await db.Stocks.Where(item => item.ProductId == productId).Select(item => item.Id).SingleOrDefaultAsync();
        return await db.StockMovements.AsNoTracking().Where(item => item.StockId == stockId).ToListAsync();
    }

    public async Task<Wine?> WineAsync(Guid productId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        return await db.Wines.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId);
    }

    public async Task<List<ProductComponent>> ComponentsAsync(Guid packId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DominioDeLaSierra.Infrastructure.Persistence.ApplicationDbContext>();
        return await db.ProductComponents.AsNoTracking().Where(item => item.PackProductId == packId).ToListAsync();
    }
}

internal sealed class UpdateDraft
{
    public string Reference { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public string Price { get; set; } = string.Empty;
    public string VatRate { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string? Vintage { get; set; }
    public string? Grape { get; set; }
    public string? Alcohol { get; set; }
    public string Stock { get; set; } = string.Empty;
    public string ComponentsJson { get; set; } = "[]";
}
