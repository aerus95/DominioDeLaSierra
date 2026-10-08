using DominioDeLaSierra.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Persistence;

internal static class CatalogConflicts
{
    public const string DuplicateCategorySlug = "Ya existe una categoría con ese slug.";
    public const string UnavailableParentCategory = "La categoría padre seleccionada ya no está disponible.";
    public const string CategoryNotFound = "La categoría no existe.";
    public const string CategorySelfParent = "Una categoría no puede ser su propia categoría padre.";
    public const string CategoryCycle = "La categoría padre seleccionada crearía un ciclo.";
    public const string CategoryHasProducts = "No se puede eliminar una categoría con productos asociados.";
    public const string CategoryHasChildren = "No se puede eliminar una categoría que tiene subcategorías.";
    public const string CategoryInUse = "No se puede eliminar la categoría porque todavía está en uso.";
    public const string DuplicateProductReference = "Ya existe un producto con esa referencia.";
    public const string DuplicateProductSlug = "Ya existe un producto con ese slug.";
    public const string UnavailableProductCategory = "La categoría seleccionada ya no está disponible.";

    public static bool TryGetCategoryMessage(DbUpdateException exception, out string message) =>
        TryGetMessage(exception, forProduct: false, out message);

    public static bool TryGetCategoryDeleteMessage(DbUpdateException exception, out string message)
    {
        var postgres = FindPostgresException(exception);
        if (postgres is null || postgres.SqlState != PostgresErrorCodes.ForeignKeyViolation)
        {
            message = string.Empty;
            return false;
        }

        message = postgres.ConstraintName switch
        {
            "FK_Products_Categories_CategoryId" => CategoryHasProducts,
            "FK_Categories_Categories_ParentCategoryId" => CategoryHasChildren,
            _ => CategoryInUse
        };
        return true;
    }

    public static bool TryGetProductMessage(DbUpdateException exception, out string message) =>
        TryGetMessage(exception, forProduct: true, out message);

    private static bool TryGetMessage(DbUpdateException exception, bool forProduct, out string message)
    {
        var postgres = FindPostgresException(exception);
        if (postgres is null)
        {
            message = string.Empty;
            return false;
        }

        if (postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            message = postgres.ConstraintName switch
            {
                "IX_Categories_Slug" => DuplicateCategorySlug,
                "IX_Products_Reference" => DuplicateProductReference,
                "IX_Products_Slug" => DuplicateProductSlug,
                "IX_Stocks_WarehouseId_ProductId" => "No se ha podido registrar el stock de este producto.",
                "IX_ProductComponents_PackProductId_ComponentProductId" => CatalogComposition.RepeatedComponentMessage,
                _ => "Esos datos ya existen."
            };
            return true;
        }

        if (postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            message = postgres.ConstraintName switch
            {
                "FK_Categories_Categories_ParentCategoryId" => UnavailableParentCategory,
                "FK_Products_Categories_CategoryId" => UnavailableProductCategory,
                "FK_ProductComponents_Products_ComponentProductId" => CatalogComposition.UnavailableComponentMessage,
                _ => forProduct
                    ? UnavailableProductCategory
                    : UnavailableParentCategory
            };
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
