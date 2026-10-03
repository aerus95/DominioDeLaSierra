using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.GetProducts;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class ProductCatalogQueries(ApplicationDbContext dbContext) : IProductCatalogQueries
{
    public async Task<PagedResult<ProductListItemDto>> GetProductsAsync(
        GetProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var products = dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .Where(product => product.Category.Active);

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
            .OrderBy(product => product.Name)
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
                product.PrimaryImageUrl))
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
        return await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .Where(product => product.Category.Active)
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
                product.PrimaryImageUrl))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
