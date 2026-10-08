using DominioDeLaSierra.Application.Categories.UpdateCategory;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class UpdateCategoryService(ApplicationDbContext dbContext) : IUpdateCategory
{
    public async Task<UpdatedCategoryDto> ExecuteAsync(UpdateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id == Guid.Empty)
        {
            throw new ArgumentException("La categoría no es válida.");
        }

        var name = CatalogText.RequireSingleLine(
            command.Name,
            CatalogLimits.CategoryNameMaxLength,
            "El nombre es obligatorio.",
            "El nombre no puede superar 150 caracteres.",
            "El nombre contiene caracteres no permitidos.");
        var slug = CatalogText.RequireSlug(
            command.Slug,
            name,
            CatalogLimits.CategorySlugMaxLength,
            "El slug no es válido.",
            "El slug no puede superar 180 caracteres.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await CategoryHierarchyLock.AcquireAsync(dbContext, cancellationToken);

        var category = await dbContext.Categories.FirstOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (category is null)
        {
            throw new ArgumentException(CatalogConflicts.CategoryNotFound);
        }

        var parents = await dbContext.Categories
            .AsNoTracking()
            .Select(item => new { item.Id, item.ParentCategoryId })
            .ToDictionaryAsync(item => item.Id, item => item.ParentCategoryId, cancellationToken);
        EnsureValidParent(category.Id, command.ParentCategoryId, parents);

        var slugTaken = await dbContext.Categories.AnyAsync(
            item => item.Slug == slug && item.Id != category.Id,
            cancellationToken);
        if (slugTaken)
        {
            throw new InvalidOperationException(CatalogConflicts.DuplicateCategorySlug);
        }

        category.Update(name, slug, command.ParentCategoryId, command.Active);
        if (CategoryHierarchyLock.BeforeSave is { } beforeSave)
        {
            await beforeSave(cancellationToken);
        }

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

            if (CatalogConflicts.TryGetCategoryMessage(exception, out var message))
            {
                throw new InvalidOperationException(message);
            }

            throw;
        }

        await transaction.CommitAsync(cancellationToken);

        return new UpdatedCategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.ParentCategoryId,
            category.Active);
    }

    private static void EnsureValidParent(
        Guid categoryId,
        Guid? parentCategoryId,
        IReadOnlyDictionary<Guid, Guid?> parents)
    {
        if (parentCategoryId is not Guid parentId)
        {
            return;
        }

        if (parentId == categoryId)
        {
            throw new ArgumentException(CatalogConflicts.CategorySelfParent);
        }

        if (!parents.ContainsKey(parentId))
        {
            throw new ArgumentException(CatalogConflicts.UnavailableParentCategory);
        }

        var visited = new HashSet<Guid>();
        var current = parentId;
        while (true)
        {
            if (current == categoryId || !visited.Add(current))
            {
                throw new ArgumentException(CatalogConflicts.CategoryCycle);
            }

            if (!parents.TryGetValue(current, out var next) || next is not Guid nextId)
            {
                return;
            }

            current = nextId;
        }
    }
}
