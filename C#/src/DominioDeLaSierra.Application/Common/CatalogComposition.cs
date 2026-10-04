using System.Globalization;
using System.Text.Json;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Common;

public static class CatalogComposition
{
    public const string EmptyPackMessage = "Un pack debe incluir al menos un componente.";
    public const string InvalidQuantityMessage = "La cantidad de un componente debe ser un entero mayor que cero.";
    public const string InvalidCompositionMessage = "La composición del pack no es válida.";
    public const string RepeatedComponentMessage = "Hay un componente repetido.";
    public const string UnavailableComponentMessage = "Uno de los componentes ya no está disponible.";

    public static bool IsEligibleComponent(ProductKind kind) =>
        kind is ProductKind.Standard or ProductKind.Wine;

    public static bool TryReadPositiveQuantity(decimal number, out int quantity)
    {
        quantity = 0;
        if (number != decimal.Truncate(number) || number < 1 || number > int.MaxValue)
        {
            return false;
        }

        quantity = (int)number;
        return true;
    }

    public static bool TryParseQuantityText(string? text, out int quantity)
    {
        quantity = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length is 0 or > 10 || !IsDigits(trimmed))
        {
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            return false;
        }

        quantity = parsed;
        return true;
    }

    public static string DuplicateComponentMessage(string reference) =>
        $"El componente «{reference}» está duplicado.";

    public static string SelfComponentMessage(string reference) =>
        $"El componente «{reference}» es el propio producto.";

    public static string PackComponentMessage(string reference) =>
        $"El componente «{reference}» es un pack.";

    public static string MissingComponentMessage(string reference) =>
        $"El componente «{reference}» no existe.";

    public static string IneligibleComponentMessage(string reference) =>
        $"El componente «{reference}» no es un producto estándar o vino.";

    public static void EnsureDraft(IReadOnlyList<(Guid ProductId, int Quantity)> components)
    {
        if (components.Count == 0)
        {
            throw new ArgumentException(EmptyPackMessage);
        }

        foreach (var component in components)
        {
            if (component.ProductId == Guid.Empty)
            {
                throw new ArgumentException(InvalidCompositionMessage);
            }

            if (component.Quantity <= 0)
            {
                throw new ArgumentException(InvalidQuantityMessage);
            }
        }

        if (components.Select(item => item.ProductId).Distinct().Count() != components.Count)
        {
            throw new ArgumentException(RepeatedComponentMessage);
        }
    }

    public static void RejectIneligible(ProductKind kind, string reference)
    {
        if (kind == ProductKind.Pack)
        {
            throw new ArgumentException(PackComponentMessage(reference));
        }

        if (!IsEligibleComponent(kind))
        {
            throw new ArgumentException(IneligibleComponentMessage(reference));
        }
    }

    public static List<(Guid ProductId, int Quantity)> ReadComponents(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000)
        {
            throw new ArgumentException(InvalidCompositionMessage);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(InvalidCompositionMessage);
            }

            var components = new List<(Guid ProductId, int Quantity)>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("productId", out var productIdField)
                    || productIdField.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(productIdField.GetString(), out var productId)
                    || productId == Guid.Empty)
                {
                    throw new ArgumentException(InvalidCompositionMessage);
                }

                if (!TryReadComponentQuantity(item, out var quantity))
                {
                    throw new ArgumentException(InvalidQuantityMessage);
                }

                components.Add((productId, quantity));
            }

            return components;
        }
        catch (JsonException)
        {
            throw new ArgumentException(InvalidCompositionMessage);
        }
    }

    private static bool TryReadComponentQuantity(JsonElement item, out int quantity)
    {
        quantity = 0;
        if (!item.TryGetProperty("quantity", out var field))
        {
            return false;
        }

        if (field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var number))
        {
            return TryReadPositiveQuantity(number, out quantity);
        }

        if (field.ValueKind == JsonValueKind.String)
        {
            return TryParseQuantityText(field.GetString(), out quantity);
        }

        return false;
    }

    private static bool IsDigits(string text)
    {
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
