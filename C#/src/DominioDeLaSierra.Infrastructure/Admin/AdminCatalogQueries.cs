using DominioDeLaSierra.Application.Admin;
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
                product.Kind))
            .ToListAsync(cancellationToken);
    }
}
