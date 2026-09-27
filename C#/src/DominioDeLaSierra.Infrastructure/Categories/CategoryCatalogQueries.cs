using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class CategoryCatalogQueries(ApplicationDbContext dbContext) : ICategoryCatalogQueries
{
    public async Task<IReadOnlyList<CategoryListItemDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.Active)
            .OrderBy(category => category.Name)
            .Select(category => new CategoryListItemDto(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId))
            .ToListAsync(cancellationToken);
    }
}
