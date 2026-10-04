using System.Globalization;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Admin;

public sealed class AdminCatalogQueries(ApplicationDbContext dbContext) : IAdminCatalogQueries
{
    public async Task<IReadOnlyList<AdminCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new AdminCategoryDto(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId,
                category.Active))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminProductDto>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Products
            .AsNoTracking()
            .OrderByDescending(product => product.UpdatedAt)
            .ThenBy(product => product.Name)
            .Select(product => new AdminProductDto(
                product.Id,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.Price,
                product.VatRate,
                product.Active,
                product.CreatedAt,
                product.UpdatedAt,
                product.CategoryId,
                product.Category.Name,
                product.Category.Slug,
                product.PrimaryImageUrl,
                product.Kind,
                dbContext.Stocks
                    .Where(stock => stock.ProductId == product.Id && stock.Warehouse.IsDefault)
                    .Select(stock => (int?)stock.Quantity)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminProductEditorDto?> GetProductEditorAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        string vintage = string.Empty;
        string grape = string.Empty;
        string alcohol = string.Empty;
        decimal? alcoholValue = null;
        if (product.Kind == ProductKind.Wine)
        {
            var wine = await dbContext.Wines
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ProductId == product.Id, cancellationToken);
            if (wine is null)
            {
                throw new InvalidOperationException(CatalogWine.MissingProfileMessage);
            }

            vintage = wine.Vintage ?? string.Empty;
            grape = wine.Grape ?? string.Empty;
            alcoholValue = wine.AlcoholPercent;
            alcohol = wine.AlcoholPercent is { } percent ? FormatAmount(percent) : string.Empty;
        }

        var components = new List<AdminEditorComponentDto>();
        var componentPairs = new List<(Guid ComponentProductId, int Quantity)>();
        var candidates = new List<AdminEditorCandidateDto>();
        if (product.Kind == ProductKind.Pack)
        {
            var rows = await dbContext.ProductComponents
                .AsNoTracking()
                .Where(item => item.PackProductId == product.Id)
                .OrderBy(item => item.ComponentProduct.Reference)
                .Select(item => new { item.ComponentProductId, item.Quantity })
                .ToListAsync(cancellationToken);
            components = rows.Select(item => new AdminEditorComponentDto(item.ComponentProductId, item.Quantity)).ToList();
            componentPairs = rows.Select(item => (item.ComponentProductId, item.Quantity)).ToList();

            candidates = await dbContext.Products
                .AsNoTracking()
                .Where(item => item.Kind == ProductKind.Standard || item.Kind == ProductKind.Wine)
                .OrderBy(item => item.Reference)
                .Select(item => new AdminEditorCandidateDto(
                    item.Id,
                    item.Active ? item.Reference + " — " + item.Name : item.Reference + " — " + item.Name + " (inactivo)"))
                .ToListAsync(cancellationToken);
        }

        int? stock = null;
        if (product.Kind != ProductKind.Pack)
        {
            stock = await dbContext.Stocks
                .AsNoTracking()
                .Where(item => item.ProductId == product.Id && item.Warehouse.IsDefault)
                .Select(item => (int?)item.Quantity)
                .FirstOrDefaultAsync(cancellationToken);
            if (stock is null)
            {
                throw new InvalidOperationException(CatalogRevision.MissingStockMessage);
            }
        }

        return new AdminProductEditorDto(
            product.Id,
            product.Reference,
            product.Name,
            product.Slug,
            product.Description,
            product.CategoryId,
            FormatAmount(product.Price),
            FormatAmount(product.VatRate),
            product.Active,
            IndexKind(product.Kind),
            CatalogRevision.Compute(
                product.Kind,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.CategoryId,
                product.Price,
                product.VatRate,
                product.Active,
                product.Kind == ProductKind.Wine ? NullIfEmpty(vintage) : null,
                product.Kind == ProductKind.Wine ? NullIfEmpty(grape) : null,
                alcoholValue,
                componentPairs),
            stock,
            vintage,
            grape,
            alcohol,
            components,
            candidates);
    }

    public async Task<IReadOnlyList<AdminComponentCandidateDto>> GetComponentCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Products
            .AsNoTracking()
            .Where(item => item.Kind == ProductKind.Standard || item.Kind == ProductKind.Wine)
            .OrderBy(item => item.Reference)
            .Select(item => new { item.Id, item.Reference, item.Name, item.Kind, item.Active })
            .ToListAsync(cancellationToken);

        return rows
            .Select(item => new AdminComponentCandidateDto(
                item.Id,
                item.Reference,
                item.Name,
                item.Kind.ToString(),
                item.Active))
            .ToList();
    }

    private static string IndexKind(ProductKind kind) => kind switch
    {
        ProductKind.Standard => "Estándar",
        ProductKind.Wine => "Vino",
        ProductKind.Pack => "Pack",
        _ => "Producto"
    };

    private static string FormatAmount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
