using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class CreateProductService(ApplicationDbContext dbContext) : ICreateProduct
{
    public async Task<Guid> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var reference = command.Reference.Trim();
        var name = command.Name.Trim();
        var description = command.Description.Trim();

        if (reference.Length is 0 or > 80)
        {
            throw new ArgumentException("La referencia es obligatoria y no puede superar 80 caracteres.", nameof(command));
        }

        if (name.Length is 0 or > 200)
        {
            throw new ArgumentException("El nombre es obligatorio y no puede superar 200 caracteres.", nameof(command));
        }

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? CatalogText.Slugify(name)
            : CatalogText.Slugify(command.Slug);

        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 220)
        {
            throw new ArgumentException("El slug es obligatorio y no puede superar 220 caracteres.", nameof(command));
        }

        if (command.Price < 0 || command.VatRate < 0)
        {
            throw new ArgumentException("El precio y el IVA no pueden ser negativos.", nameof(command));
        }

        var categoryExists = await dbContext.Categories.AnyAsync(
            category => category.Id == command.CategoryId,
            cancellationToken);
        if (!categoryExists)
        {
            throw new ArgumentException("La categoría no existe.", nameof(command));
        }

        var referenceTaken = await dbContext.Products.AnyAsync(product => product.Reference == reference, cancellationToken);
        if (referenceTaken)
        {
            throw new InvalidOperationException($"Ya existe un producto con la referencia «{reference}».");
        }

        var slugTaken = await dbContext.Products.AnyAsync(product => product.Slug == slug, cancellationToken);
        if (slugTaken)
        {
            throw new InvalidOperationException($"Ya existe un producto con el slug «{slug}».");
        }

        var now = DateTimeOffset.UtcNow;
        var product = new Product(
            Guid.NewGuid(),
            reference,
            name,
            slug,
            description,
            command.CategoryId,
            decimal.Round(command.Price, 2, MidpointRounding.AwayFromZero),
            decimal.Round(command.VatRate, 2, MidpointRounding.AwayFromZero),
            command.Active,
            now,
            now);

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return product.Id;
    }
}
