using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Common;

public static class CatalogRevision
{
    public const string ProductChangedMessage =
        "El producto ha cambiado desde que abriste la edición. Vuelve a abrirlo e inténtalo de nuevo.";

    public const string StockChangedMessage =
        "El stock ha cambiado desde que abriste la edición. Vuelve a abrir el producto e inténtalo de nuevo.";

    public const string MissingStockMessage = "El stock de este producto no está disponible.";

    public static string Compute(
        ProductKind kind,
        string reference,
        string name,
        string slug,
        string description,
        Guid categoryId,
        decimal price,
        decimal vatRate,
        bool active,
        string? vintage,
        string? grape,
        decimal? alcoholPercent,
        IEnumerable<(Guid ComponentProductId, int Quantity)> components)
    {
        var builder = new StringBuilder();
        Append(builder, kind.ToString());
        Append(builder, reference);
        Append(builder, name);
        Append(builder, slug);
        Append(builder, description);
        Append(builder, categoryId.ToString("D"));
        Append(builder, price.ToString("0.00", CultureInfo.InvariantCulture));
        Append(builder, vatRate.ToString("0.00", CultureInfo.InvariantCulture));
        Append(builder, active ? "true" : "false");
        Append(builder, vintage);
        Append(builder, grape);
        Append(builder, alcoholPercent?.ToString("0.00", CultureInfo.InvariantCulture));
        foreach (var component in components.OrderBy(item => item.ComponentProductId))
        {
            Append(builder, component.ComponentProductId.ToString("D"));
            Append(builder, component.Quantity.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
