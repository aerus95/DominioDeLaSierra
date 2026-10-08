using System.Globalization;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Admin;

public sealed class AdminCatalogQueries(ApplicationDbContext dbContext) : IAdminCatalogQueries
{
    public async Task<AdminListResult<AdminCategoryDto>> GetCategoriesAsync(
        AdminCategoryListQuery query,
        CancellationToken cancellationToken = default)
    {
        var categories = dbContext.Categories.AsNoTracking();
        var search = AdminListSearch.Normalize(query.Search);
        if (search is not null)
        {
            var pattern = LikePattern(search);
            categories = categories.Where(category =>
                EF.Functions.ILike(category.Name, pattern, LikeEscape)
                || EF.Functions.ILike(category.Slug, pattern, LikeEscape));
        }

        if (query.Active is bool active)
        {
            categories = categories.Where(category => category.Active == active);
        }

        if (query.RootsOnly)
        {
            categories = categories.Where(category => category.ParentCategoryId == null);
        }
        else if (query.ParentCategoryId is Guid parentId && parentId != Guid.Empty)
        {
            categories = categories.Where(category => category.ParentCategoryId == parentId);
        }

        var totalCount = await categories.CountAsync(cancellationToken);
        var items = await categories
            .OrderBy(category => category.Name)
            .Select(category => new AdminCategoryDto(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId,
                category.Active))
            .ToListAsync(cancellationToken);

        return new AdminListResult<AdminCategoryDto>(items, totalCount);
    }

    public async Task<AdminListResult<AdminProductDto>> GetProductsAsync(
        AdminProductListQuery query,
        CancellationToken cancellationToken = default)
    {
        var products = dbContext.Products.AsNoTracking();
        var search = AdminListSearch.Normalize(query.Search);
        if (search is not null)
        {
            var pattern = LikePattern(search);
            products = products.Where(product =>
                EF.Functions.ILike(product.Name, pattern, LikeEscape)
                || EF.Functions.ILike(product.Reference, pattern, LikeEscape)
                || EF.Functions.ILike(product.Description, pattern, LikeEscape)
                || (product.Kind == ProductKind.Pack && product.Components.Any(component =>
                    EF.Functions.ILike(component.ComponentProduct.Name, pattern, LikeEscape))));
        }

        if (query.CategoryId is Guid categoryId && categoryId != Guid.Empty)
        {
            products = products.Where(product => product.CategoryId == categoryId);
        }

        if (query.Kind is ProductKind kind)
        {
            products = products.Where(product => product.Kind == kind);
        }

        if (query.Active is bool active)
        {
            products = products.Where(product => product.Active == active);
        }

        var totalCount = await products.CountAsync(cancellationToken);
        var items = await products
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

        return new AdminListResult<AdminProductDto>(items, totalCount);
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

    private const string LikeEscape = "\\";

    private static string LikePattern(string search)
    {
        var escaped = search
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
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
