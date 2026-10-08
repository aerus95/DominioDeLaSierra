using DominioDeLaSierra.Application.Categories.DeleteCategory;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class DeleteCategoryService(ApplicationDbContext dbContext) : IDeleteCategory
{
    public async Task<DeletedCategoryDto> ExecuteAsync(DeleteCategoryCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id == Guid.Empty)
        {
            throw new ArgumentException("La categoría no es válida.");
        }

        var category = await dbContext.Categories.FirstOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (category is null)
        {
            throw new ArgumentException(CatalogConflicts.CategoryNotFound);
        }

        var hasProducts = await dbContext.Products.AnyAsync(product => product.CategoryId == category.Id, cancellationToken);
        if (hasProducts)
        {
            throw new InvalidOperationException(CatalogConflicts.CategoryHasProducts);
        }

        var hasChildren = await dbContext.Categories.AnyAsync(
            item => item.ParentCategoryId == category.Id,
            cancellationToken);
        if (hasChildren)
        {
            throw new InvalidOperationException(CatalogConflicts.CategoryHasChildren);
        }

        var deleted = new DeletedCategoryDto(category.Id, category.Name);
        dbContext.Categories.Remove(category);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (exception is DbUpdateConcurrencyException)
            {
                throw new ArgumentException(CatalogConflicts.CategoryNotFound);
            }

            if (CatalogConflicts.TryGetCategoryDeleteMessage(exception, out var message))
            {
                throw new InvalidOperationException(message);
            }

            throw;
        }

        return deleted;
    }
}
