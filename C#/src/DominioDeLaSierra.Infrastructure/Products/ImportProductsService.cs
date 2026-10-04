using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Inventory;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class ImportProductsService(ApplicationDbContext dbContext) : IImportProducts
{
    public async Task<ProductImportReport> ExecuteAsync(
        ImportProductsCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var inspection = await ProductImportInspection.InspectAsync(
                dbContext,
                command.Content,
                command.Length,
                cancellationToken);
            if (inspection.Batch is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return inspection.Report;
            }

            var warehouseId = await InventoryStock.FindActiveDefaultWarehouseIdAsync(dbContext, cancellationToken);
            if (warehouseId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ProductImportReport.Document(InventoryStock.MissingWarehouseMessage);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var row in inspection.Batch)
            {
                dbContext.Products.Add(new Product(
                    row.Id,
                    row.Reference,
                    row.Name,
                    row.Slug,
                    row.Description,
                    row.CategoryId,
                    row.Price,
                    row.VatRate,
                    row.Active,
                    now,
                    now,
                    row.Kind));

                if (row.Kind == ProductKind.Wine)
                {
                    dbContext.Wines.Add(new Wine(row.Id, row.Vintage, row.Grape, row.AlcoholPercent));
                }
            }

            foreach (var row in inspection.Batch.Where(product => product.Kind == ProductKind.Pack))
            {
                foreach (var component in row.Components)
                {
                    dbContext.ProductComponents.Add(new ProductComponent(
                        Guid.NewGuid(),
                        row.Id,
                        component.ProductId,
                        component.Quantity));
                }
            }

            foreach (var row in inspection.Batch)
            {
                InventoryStock.AddOpeningBalance(
                    dbContext,
                    warehouseId.Value,
                    row.Id,
                    row.Kind,
                    0,
                    now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return inspection.Report with { Imported = true };
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var message = IsCatalogConflict(exception)
                ? "El catálogo ha cambiado durante la importación. Vuelve a validar el fichero."
                : "No se ha podido completar la importación.";
            return ProductImportReport.Document(message);
        }
    }

    private static bool IsCatalogConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
            {
                return true;
            }
        }

        return false;
    }
}
