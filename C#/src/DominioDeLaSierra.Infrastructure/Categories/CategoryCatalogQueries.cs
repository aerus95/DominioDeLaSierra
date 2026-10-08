using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class CategoryCatalogQueries(ApplicationDbContext dbContext) : ICategoryCatalogQueries
{
    public async Task<IReadOnlyList<CategoryListItemDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var visibleCategoryIds = await PublicCategoryVisibility.GetVisibleIdsAsync(dbContext, cancellationToken);
        if (visibleCategoryIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Categories
            .AsNoTracking()
            .Where(category => visibleCategoryIds.Contains(category.Id))
            .OrderBy(category => category.Name)
            .Select(category => new CategoryListItemDto(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId))
            .ToListAsync(cancellationToken);
    }
}
