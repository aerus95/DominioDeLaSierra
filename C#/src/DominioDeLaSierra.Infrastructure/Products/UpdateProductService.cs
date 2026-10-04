using System.Data;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.UpdateProduct;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Inventory;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class UpdateProductService(ApplicationDbContext dbContext) : IUpdateProduct
{
    public async Task ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ProductId == Guid.Empty)
        {
            throw new ArgumentException("El producto no es válido.");
        }

        if (string.IsNullOrWhiteSpace(command.FichaRevision))
        {
            throw new InvalidOperationException(CatalogRevision.ProductChangedMessage);
        }

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

        if (string.IsNullOrWhiteSpace(command.CategoryId) || !Guid.TryParse(command.CategoryId, out var categoryId) || categoryId == Guid.Empty)
        {
            throw new ArgumentException(string.IsNullOrWhiteSpace(command.CategoryId)
                ? "Selecciona una categoría."
                : "La categoría seleccionada no es válida.");
        }

        if (string.IsNullOrWhiteSpace(command.Price))
        {
            throw new ArgumentException("El precio es obligatorio.");
        }

        if (!CatalogNumbers.TryParsePrice(command.Price, out var price) || !CatalogNumbers.IsStorablePrice(price))
        {
            throw new ArgumentException("El precio indicado no es válido.");
        }

        if (string.IsNullOrWhiteSpace(command.VatRate))
        {
            throw new ArgumentException("El IVA es obligatorio.");
        }

        if (!CatalogNumbers.TryParseVatRate(command.VatRate, out var vatRate) || !CatalogNumbers.IsStorableVatRate(vatRate))
        {
            throw new ArgumentException("El IVA indicado no es válido.");
        }

        if (!command.ActiveSpecified || command.Active is null)
        {
            throw new ArgumentException("El estado del producto no es válido.");
        }

        var active = command.Active.Value;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await LockRowAsync(
                """SELECT "Id" FROM "Products" WHERE "Id" = @id FOR UPDATE""",
                ("id", command.ProductId),
                cancellationToken))
            {
                throw new InvalidOperationException("El producto no existe.");
            }

            var product = await dbContext.Products
                .FirstOrDefaultAsync(item => item.Id == command.ProductId, cancellationToken);
            if (product is null)
            {
                throw new InvalidOperationException("El producto no existe.");
            }

            var wine = product.Kind == ProductKind.Wine
                ? await dbContext.Wines.FirstOrDefaultAsync(item => item.ProductId == product.Id, cancellationToken)
                : null;
            var components = product.Kind == ProductKind.Pack
                ? await dbContext.ProductComponents
                    .Where(item => item.PackProductId == product.Id)
                    .ToListAsync(cancellationToken)
                : [];

            if (product.Kind == ProductKind.Wine && wine is null)
            {
                throw new InvalidOperationException(CatalogWine.MissingProfileMessage);
            }

            var revision = CatalogRevision.Compute(
                product.Kind,
                product.Reference,
                product.Name,
                product.Slug,
                product.Description,
                product.CategoryId,
                product.Price,
                product.VatRate,
                product.Active,
                wine?.Vintage,
                wine?.Grape,
                wine?.AlcoholPercent,
                components.Select(item => (item.ComponentProductId, item.Quantity)));
            if (!string.Equals(revision, command.FichaRevision.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(CatalogRevision.ProductChangedMessage);
            }

            var categoryExists = await dbContext.Categories.AnyAsync(item => item.Id == categoryId, cancellationToken);
            if (!categoryExists)
            {
                throw new ArgumentException(CatalogConflicts.UnavailableProductCategory);
            }

            var referenceTaken = await dbContext.Products.AnyAsync(
                item => item.Reference == reference && item.Id != product.Id,
                cancellationToken);
            if (referenceTaken)
            {
                throw new InvalidOperationException(CatalogConflicts.DuplicateProductReference);
            }

            var slugTaken = await dbContext.Products.AnyAsync(
                item => item.Slug == slug && item.Id != product.Id,
                cancellationToken);
            if (slugTaken)
            {
                throw new InvalidOperationException(CatalogConflicts.DuplicateProductSlug);
            }

            var now = DateTimeOffset.UtcNow;
            product.UpdateCatalog(
                reference,
                name,
                slug,
                description,
                categoryId,
                decimal.Round(price, 2, MidpointRounding.AwayFromZero),
                decimal.Round(vatRate, 2, MidpointRounding.AwayFromZero),
                active,
                now);

            switch (product.Kind)
            {
                case ProductKind.Wine:
                    wine!.Update(
                        CatalogWine.NormalizeVintage(command.Vintage),
                        CatalogWine.NormalizeGrape(command.Grape),
                        ReadAlcohol(command.Alcohol));
                    await AdjustStockAsync(product, command, now, cancellationToken);
                    break;
                case ProductKind.Standard:
                    await AdjustStockAsync(product, command, now, cancellationToken);
                    break;
                case ProductKind.Pack:
                    if (command.StockSpecified && !string.IsNullOrWhiteSpace(command.Stock))
                    {
                        throw new ArgumentException(InventoryStock.PackStockMessage);
                    }

                    await UpdateComponentsAsync(product, components, command.ComponentsJson, command.ComponentsSpecified, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("El producto no es válido.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!CatalogConflicts.TryGetProductMessage(exception, out var message))
            {
                message = "No se ha podido guardar el producto.";
            }

            throw new InvalidOperationException(message);
        }
    }

    private async Task AdjustStockAsync(
        Product product,
        UpdateProductCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!command.StockSpecified)
        {
            throw new ArgumentException("El stock indicado no es válido.");
        }

        if (!CatalogNumbers.TryParseStockQuantity(command.LoadedStock, out var loadedStock, out var loadedError))
        {
            throw new ArgumentException(loadedError ?? "El stock indicado no es válido.");
        }

        if (!CatalogNumbers.TryParseStockQuantity(command.Stock, out var desired, out var stockError))
        {
            throw new ArgumentException(stockError ?? "El stock indicado no es válido.");
        }

        var warehouseId = await InventoryStock.FindActiveDefaultWarehouseIdAsync(dbContext, cancellationToken);
        if (warehouseId is null)
        {
            throw new InvalidOperationException(InventoryStock.MissingWarehouseMessage);
        }

        var locked = await LockRowAsync(
            """SELECT "Id" FROM "Stocks" WHERE "ProductId" = @productId AND "WarehouseId" = @warehouseId FOR UPDATE""",
            ("productId", product.Id),
            ("warehouseId", warehouseId.Value),
            cancellationToken);
        if (!locked)
        {
            throw new InvalidOperationException(CatalogRevision.MissingStockMessage);
        }

        var stock = await dbContext.Stocks.FirstOrDefaultAsync(
            item => item.ProductId == product.Id && item.WarehouseId == warehouseId.Value,
            cancellationToken);
        if (stock is null)
        {
            throw new InvalidOperationException(CatalogRevision.MissingStockMessage);
        }

        if (stock.Quantity != loadedStock)
        {
            throw new InvalidOperationException(CatalogRevision.StockChangedMessage);
        }

        var delta = desired - stock.Quantity;
        if (delta == 0)
        {
            return;
        }

        stock.SetQuantity(desired, now);
        dbContext.StockMovements.Add(new StockMovement(
            Guid.NewGuid(),
            stock.Id,
            StockMovementType.Adjustment,
            delta,
            now,
            InventoryLimits.AdjustmentNote));
    }

    private async Task UpdateComponentsAsync(
        Product pack,
        List<ProductComponent> current,
        string? componentsJson,
        bool componentsSpecified,
        CancellationToken cancellationToken)
    {
        if (!componentsSpecified)
        {
            throw new ArgumentException(CatalogComposition.InvalidCompositionMessage);
        }

        var incoming = CatalogComposition.ReadComponents(componentsJson);
        CatalogComposition.EnsureDraft(incoming);

        if (incoming.Any(item => item.ProductId == pack.Id))
        {
            throw new ArgumentException(CatalogComposition.SelfComponentMessage(pack.Reference));
        }

        var ids = incoming.Select(item => item.ProductId).ToArray();
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

        var desired = incoming.ToDictionary(item => item.ProductId, item => item.Quantity);
        foreach (var component in current)
        {
            if (!desired.TryGetValue(component.ComponentProductId, out var quantity))
            {
                dbContext.ProductComponents.Remove(component);
                continue;
            }

            if (component.Quantity != quantity)
            {
                component.ChangeQuantity(quantity);
            }
        }

        var currentIds = current.Select(item => item.ComponentProductId).ToHashSet();
        foreach (var component in incoming)
        {
            if (currentIds.Contains(component.ProductId))
            {
                continue;
            }

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

    private async Task<bool> LockRowAsync(
        string sql,
        (string Name, Guid Value) first,
        CancellationToken cancellationToken)
    {
        return await LockRowAsync(sql, first, null, cancellationToken);
    }

    private async Task<bool> LockRowAsync(
        string sql,
        (string Name, Guid Value) first,
        (string Name, Guid Value)? second,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("No se ha podido guardar el producto.");
        command.CommandText = sql;
        AddParameter(command, first.Name, first.Value);
        if (second is { } extra)
        {
            AddParameter(command, extra.Name, extra.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
