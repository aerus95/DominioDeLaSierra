using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Inventory;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class CreateProductService(
    ApplicationDbContext dbContext,
    IConfiguration configuration) : ICreateProduct
{
    public async Task<CreatedProductDto> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.Kind))
        {
            throw new ArgumentException("El tipo de producto no es válido.");
        }

        var kind = command.Kind;
        var reference = CatalogText.RequireSingleLine(
            command.Reference,
            CatalogLimits.ProductReferenceMaxLength,
            "La referencia es obligatoria.",
            "La referencia no puede superar 80 caracteres.",
            "La referencia contiene caracteres no permitidos.");
        var name = CatalogText.RequireSingleLine(
            command.Name,
            CatalogLimits.ProductNameMaxLength,
            "El nombre es obligatorio.",
            "El nombre no puede superar 200 caracteres.",
            "El nombre contiene caracteres no permitidos.");
        var description = CatalogText.NormalizeOptionalText(
            command.Description,
            CatalogLimits.ProductDescriptionMaxLength,
            allowLineBreaks: true,
            $"La descripción no puede superar {CatalogLimits.ProductDescriptionMaxLength} caracteres.",
            "La descripción contiene caracteres no permitidos.");
        var slug = CatalogText.RequireSlug(
            command.Slug,
            name,
            CatalogLimits.ProductSlugMaxLength,
            "El slug no es válido.",
            "El slug no puede superar 220 caracteres.");

        if (!CatalogNumbers.IsStorablePrice(command.Price))
        {
            throw new ArgumentException("El precio indicado no es válido.", nameof(command));
        }

        if (!CatalogNumbers.IsStorableVatRate(command.VatRate))
        {
            throw new ArgumentException("El IVA indicado no es válido.", nameof(command));
        }

        string? vintage = null;
        string? grape = null;
        decimal? alcohol = null;
        List<(Guid ProductId, int Quantity)>? components = null;
        if (kind is ProductKind.Standard or ProductKind.Wine)
        {
            InventoryStock.EnsureQuantity(command.InitialStock);
        }

        if (kind == ProductKind.Wine)
        {
            vintage = CatalogWine.NormalizeVintage(command.Vintage);
            grape = CatalogWine.NormalizeGrape(command.Grape);
            alcohol = ReadAlcohol(command.Alcohol);
        }

        if (kind == ProductKind.Pack)
        {
            if (string.IsNullOrWhiteSpace(command.ComponentsJson))
            {
                throw new ArgumentException(CatalogComposition.EmptyPackMessage);
            }

            components = CatalogComposition.ReadComponents(command.ComponentsJson);
            CatalogComposition.EnsureDraft(components);
        }

        byte[]? preparedImage = null;
        if (command.ImageContent is not null && command.ImageLength > 0)
        {
            preparedImage = await ProductImageContent.PrepareAsync(
                command.ImageContent,
                command.ImageLength,
                cancellationToken);
        }

        var committed = false;
        string? writtenPath = null;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid? warehouseId = null;
            if (kind is ProductKind.Standard or ProductKind.Wine)
            {
                warehouseId = await InventoryStock.FindActiveDefaultWarehouseIdAsync(dbContext, cancellationToken);
                if (warehouseId is null)
                {
                    throw new InvalidOperationException(InventoryStock.MissingWarehouseMessage);
                }
            }

            var categoryExists = await dbContext.Categories.AnyAsync(
                category => category.Id == command.CategoryId,
                cancellationToken);
            if (!categoryExists)
            {
                throw new ArgumentException(CatalogConflicts.UnavailableProductCategory, nameof(command));
            }

            var referenceTaken = await dbContext.Products.AnyAsync(product => product.Reference == reference, cancellationToken);
            if (referenceTaken)
            {
                throw new InvalidOperationException(CatalogConflicts.DuplicateProductReference);
            }

            var slugTaken = await dbContext.Products.AnyAsync(product => product.Slug == slug, cancellationToken);
            if (slugTaken)
            {
                throw new InvalidOperationException(CatalogConflicts.DuplicateProductSlug);
            }

            string? primaryImageUrl = null;
            if (preparedImage is not null)
            {
                primaryImageUrl = await WritePreparedImageAsync(preparedImage, path => writtenPath = path, cancellationToken);
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
                now,
                kind,
                primaryImageUrl);

            dbContext.Products.Add(product);
            if (kind == ProductKind.Wine)
            {
                dbContext.Wines.Add(new Wine(product.Id, vintage, grape, alcohol));
            }

            if (kind is ProductKind.Standard or ProductKind.Wine)
            {
                InventoryStock.AddOpeningBalance(
                    dbContext,
                    warehouseId!.Value,
                    product.Id,
                    kind,
                    command.InitialStock,
                    now);
            }

            if (kind == ProductKind.Pack)
            {
                await AddComponentsAsync(product, components!, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new CreatedProductDto(
                product.Id,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.CategoryId,
                product.Price,
                product.VatRate,
                product.Active,
                product.CreatedAt,
                product.UpdatedAt);
        }
        catch (DbUpdateException exception) when (CatalogConflicts.TryGetProductMessage(exception, out var message))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(message);
        }
        finally
        {
            if (!committed && writtenPath is not null)
            {
                ManagedProductImageFiles.DeleteIfExists(writtenPath);
            }
        }
    }

    private async Task<string> WritePreparedImageAsync(
        byte[] preparedImage,
        Action<string> rememberPath,
        CancellationToken cancellationToken)
    {
        var fullPath = string.Empty;
        try
        {
            var productsDirectory = ManagedProductImageFiles.ProductsDirectory(configuration);
            fullPath = ManagedProductImageFiles.AllocateWebpPath(productsDirectory, out var publicUrl);
            await ProductImageContent.WriteAsync(fullPath, preparedImage, cancellationToken);
            rememberPath(fullPath);
            return publicUrl;
        }
        catch (IOException)
        {
            if (fullPath.Length > 0)
            {
                ManagedProductImageFiles.DeleteIfExists(fullPath);
            }

            throw new InvalidOperationException(ProductImageContent.StorageFailureMessage);
        }
        catch (UnauthorizedAccessException)
        {
            if (fullPath.Length > 0)
            {
                ManagedProductImageFiles.DeleteIfExists(fullPath);
            }

            throw new InvalidOperationException(ProductImageContent.StorageFailureMessage);
        }
    }

    private async Task AddComponentsAsync(
        Product pack,
        IReadOnlyList<(Guid ProductId, int Quantity)> components,
        CancellationToken cancellationToken)
    {
        if (components.Any(item => item.ProductId == pack.Id))
        {
            throw new ArgumentException(CatalogComposition.SelfComponentMessage(pack.Reference));
        }

        var ids = components.Select(item => item.ProductId).ToArray();
        var products = await dbContext.Products
            .Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.Reference, item.Kind })
            .ToListAsync(cancellationToken);
        if (products.Count != ids.Length)
        {
            throw new ArgumentException(CatalogComposition.UnavailableComponentMessage);
        }

        foreach (var component in products)
        {
            CatalogComposition.RejectIneligible(component.Kind, component.Reference);
        }

        foreach (var component in components)
        {
            dbContext.ProductComponents.Add(new ProductComponent(
                Guid.NewGuid(),
                pack.Id,
                component.ProductId,
                component.Quantity));
        }
    }

    private static decimal? ReadAlcohol(string? text)
    {
        if (!CatalogNumbers.TryParseOptionalAlcohol(text, out var alcohol, out var error))
        {
            throw new ArgumentException(error ?? "El grado alcohólico debe estar entre 0 y 99,99.");
        }

        return alcohol;
    }
}
