using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class CreateCategoryService(ApplicationDbContext dbContext) : ICreateCategory
{
    public async Task<CreatedCategoryDto> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken = default)
    {
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

        if (command.ParentCategoryId is { } parentId)
        {
            var parentExists = await dbContext.Categories.AnyAsync(category => category.Id == parentId, cancellationToken);
            if (!parentExists)
            {
                throw new ArgumentException(CatalogConflicts.UnavailableParentCategory, nameof(command));
            }
        }

        var slugTaken = await dbContext.Categories.AnyAsync(category => category.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new InvalidOperationException(CatalogConflicts.DuplicateCategorySlug);
        }

        var category = new Category(Guid.NewGuid(), name, slug, command.ParentCategoryId, command.Active);
        dbContext.Categories.Add(category);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (CatalogConflicts.TryGetCategoryMessage(exception, out var message))
        {
            throw new InvalidOperationException(message);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CreatedCategoryDto(
            category.Id,
            category.Name,
            category.Slug,
            category.ParentCategoryId,
            category.Active);
    }
}
