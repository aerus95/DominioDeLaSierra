using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.GetProducts;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Categories;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class ProductCatalogQueries(ApplicationDbContext dbContext) : IProductCatalogQueries
{
    public async Task<PagedResult<ProductListItemDto>> GetProductsAsync(
        GetProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var visibleCategoryIds = await VisibleCategoryIdsAsync(cancellationToken);
        if (visibleCategoryIds is null)
        {
            return new PagedResult<ProductListItemDto>([], query.Page, query.PageSize, 0, 0);
        }

        var products = dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .Where(product => visibleCategoryIds.Contains(product.CategoryId));

        if (query.Search is not null)
        {
            var pattern = $"%{query.Search}%";
            products = products.Where(product =>
                EF.Functions.ILike(product.Name, pattern)
                || EF.Functions.ILike(product.Reference, pattern)
                || EF.Functions.ILike(product.Slug, pattern));
        }

        if (query.Category is not null)
        {
            products = products.Where(product => product.Category.Slug == query.Category);
        }

        if (query.MinPrice is not null)
        {
            products = products.Where(product => product.Price >= query.MinPrice);
        }

        if (query.MaxPrice is not null)
        {
            products = products.Where(product => product.Price <= query.MaxPrice);
        }

        var totalItems = await products.CountAsync(cancellationToken);
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)query.PageSize);

        var items = await products
            .OrderBy(product => product.Kind == ProductKind.Wine
                ? 0
                : product.Kind == ProductKind.Pack
                    ? 1
                    : 2)
            .ThenBy(product => product.Price)
            .ThenBy(product => product.Reference)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(product => new ProductListItemDto(
                product.Id,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.Price,
                product.VatRate,
                product.CategoryId,
                product.Category.Name,
                product.Category.Slug,
                product.PrimaryImageUrl,
                product.Kind == ProductKind.Wine ? "Wine" : product.Kind == ProductKind.Pack ? "Pack" : "Standard",
                product.Kind == ProductKind.Wine ? product.Wine!.Grape : null,
                product.Kind == ProductKind.Wine ? product.Wine!.AlcoholPercent : null))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductListItemDto>(
            items,
            query.Page,
            query.PageSize,
            totalItems,
            totalPages);
    }

    public async Task<ProductListItemDto?> GetProductBySlugAsync(
        GetProductBySlugQuery query,
        CancellationToken cancellationToken = default)
    {
        var visibleCategoryIds = await VisibleCategoryIdsAsync(cancellationToken);
        if (visibleCategoryIds is null)
        {
            return null;
        }

        return await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .Where(product => visibleCategoryIds.Contains(product.CategoryId))
            .Where(product => product.Slug == query.Slug)
            .Select(product => new ProductListItemDto(
                product.Id,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.Price,
                product.VatRate,
                product.CategoryId,
                product.Category.Name,
                product.Category.Slug,
                product.PrimaryImageUrl,
                product.Kind == ProductKind.Wine ? "Wine" : product.Kind == ProductKind.Pack ? "Pack" : "Standard",
                product.Kind == ProductKind.Wine ? product.Wine!.Grape : null,
                product.Kind == ProductKind.Wine ? product.Wine!.AlcoholPercent : null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PackComponentDto>?> GetPackComponentsAsync(
        GetPackComponentsQuery query,
        CancellationToken cancellationToken = default)
    {
        var visibleCategoryIds = await VisibleCategoryIdsAsync(cancellationToken);
        if (visibleCategoryIds is null)
        {
            return null;
        }

        var pack = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .Where(product => visibleCategoryIds.Contains(product.CategoryId))
            .Where(product => product.Slug == query.Slug)
            .Select(product => new { product.Id, product.Kind })
            .FirstOrDefaultAsync(cancellationToken);

        if (pack is null)
        {
            return null;
        }

        if (pack.Kind != ProductKind.Pack)
        {
            return [];
        }

        return await dbContext.ProductComponents
            .AsNoTracking()
            .Where(component => component.PackProductId == pack.Id)
            .OrderBy(component => component.ComponentProduct.Name)
            .ThenBy(component => component.ComponentProduct.Reference)
            .Select(component => new PackComponentDto(
                component.ComponentProductId,
                component.ComponentProduct.Name,
                component.ComponentProduct.Description,
                component.ComponentProduct.PrimaryImageUrl,
                component.Quantity))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>?> VisibleCategoryIdsAsync(CancellationToken cancellationToken)
    {
        var visibleCategoryIds = await PublicCategoryVisibility.GetVisibleIdsAsync(dbContext, cancellationToken);
        return visibleCategoryIds.Count == 0 ? null : visibleCategoryIds;
    }
}
