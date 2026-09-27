using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

public sealed class CreateCategoryService(ApplicationDbContext dbContext) : ICreateCategory
{
    public async Task<Guid> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        if (name.Length is 0 or > 150)
        {
            throw new ArgumentException("El nombre es obligatorio y no puede superar 150 caracteres.", nameof(command));
        }

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? CatalogText.Slugify(name)
            : CatalogText.Slugify(command.Slug);

        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 180)
        {
            throw new ArgumentException("El slug es obligatorio y no puede superar 180 caracteres.", nameof(command));
        }

        if (command.ParentCategoryId is { } parentId)
        {
            var parentExists = await dbContext.Categories.AnyAsync(category => category.Id == parentId, cancellationToken);
            if (!parentExists)
            {
                throw new ArgumentException("La categoría padre no existe.", nameof(command));
            }
        }

        var slugTaken = await dbContext.Categories.AnyAsync(category => category.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new InvalidOperationException($"Ya existe una categoría con el slug «{slug}».");
        }

        var category = new Category(Guid.NewGuid(), name, slug, command.ParentCategoryId, command.Active);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}
