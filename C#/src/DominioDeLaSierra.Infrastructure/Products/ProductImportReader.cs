using System.Text.Json;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Infrastructure.Products;

internal static class ProductImportReader
{
    private const decimal MaxPrice = 9_999_999_999.99m;
    private const decimal MaxVatRate = 999.99m;
    private const decimal MaxAlcohol = 99.99m;

    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal) { "products" };
    private static readonly HashSet<string> ProductProperties = new(StringComparer.Ordinal)
    {
        "reference", "name", "slug", "description", "category", "price", "vatRate", "active", "kind", "wine", "components"
    };
    private static readonly HashSet<string> WineProperties = new(StringComparer.Ordinal)
    {
        "vintage", "grape", "alcoholPercent"
    };
    private static readonly HashSet<string> ComponentProperties = new(StringComparer.Ordinal)
    {
        "reference", "quantity"
    };

    public static async Task<ProductImportReadResult> ReadAsync(
        Stream content,
        long length,
        CancellationToken cancellationToken)
    {
        var result = new ProductImportReadResult();
        if (length > ProductImportLimits.MaxJsonBytes)
        {
            result.DocumentErrors.Add("El fichero supera 1 MB.");
            return Stop(result);
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > ProductImportLimits.MaxJsonBytes)
            {
                result.DocumentErrors.Add("El fichero supera 1 MB.");
                return Stop(result);
            }

            buffer.Write(chunk, 0, read);
        }

        if (total == 0)
        {
            result.DocumentErrors.Add("El documento está vacío.");
            return Stop(result);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 32
            });
        }
        catch (JsonException)
        {
            result.DocumentErrors.Add("El JSON no es válido.");
            return Stop(result);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.DocumentErrors.Add("El documento debe ser un objeto JSON con la propiedad «products».");
                return Stop(result);
            }

            CollectUnknownProperties(root, RootProperties, result.DocumentErrors);
            if (!root.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            {
                result.DocumentErrors.Add("El documento debe contener la lista «products».");
                return Stop(result);
            }

            if (products.GetArrayLength() == 0)
            {
                result.DocumentErrors.Add("El documento no contiene productos.");
                return Stop(result);
            }

            if (products.GetArrayLength() > ProductImportLimits.MaxProducts)
            {
                result.DocumentErrors.Add("El fichero supera 500 productos.");
                return Stop(result);
            }

            var row = 0;
            foreach (var item in products.EnumerateArray())
            {
                row++;
                result.Drafts.Add(ReadProduct(item, row));
            }
        }

        FlagDuplicateReferences(result.Drafts);
        FlagDuplicateSlugs(result.Drafts);
        return result;
    }

    private static ProductImportReadResult Stop(ProductImportReadResult result)
    {
        result.Stop = true;
        return result;
    }

    private static ProductImportDraft ReadProduct(JsonElement item, int row)
    {
        var draft = new ProductImportDraft { Row = row };
        if (item.ValueKind != JsonValueKind.Object)
        {
            draft.Issues.Add("El producto debe ser un objeto JSON.");
            return draft;
        }

        CollectUnknownProperties(item, ProductProperties, draft.Issues);
        ReadReference(item, draft);
        ReadName(item, draft);
        ReadSlug(item, draft);
        ReadDescription(item, draft);
        ReadCategory(item, draft);
        ReadPrice(item, draft);
        ReadVatRate(item, draft);
        ReadActive(item, draft);
        ReadKind(item, draft);
        ReadWine(item, draft);
        ReadComponents(item, draft);
        ApplyKindRules(draft);
        FlagDuplicateComponents(draft);
        return draft;
    }

    private static void ReadReference(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "reference", out var field))
        {
            draft.Issues.Add("La referencia es obligatoria.");
            return;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("La referencia debe ser un texto.");
            return;
        }

        var reference = field.GetString()?.Trim() ?? string.Empty;
        if (reference.Length is 0 or > 80)
        {
            draft.Issues.Add("La referencia es obligatoria y no puede superar 80 caracteres.");
            return;
        }

        draft.Reference = reference;
        draft.ReferenceValid = true;
    }

    private static void ReadName(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "name", out var field))
        {
            draft.Issues.Add("El nombre es obligatorio.");
            return;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("El nombre debe ser un texto.");
            return;
        }

        var name = field.GetString()?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            draft.Issues.Add("El nombre es obligatorio y no puede superar 200 caracteres.");
            return;
        }

        draft.Name = name;
        draft.NameValid = true;
    }

    private static void ReadSlug(JsonElement item, ProductImportDraft draft)
    {
        string? requested = null;
        if (TryGetField(item, "slug", out var field) && field.ValueKind != JsonValueKind.Null)
        {
            if (field.ValueKind != JsonValueKind.String)
            {
                draft.Issues.Add("El slug debe ser un texto.");
                return;
            }

            requested = field.GetString();
        }

        var slug = string.IsNullOrWhiteSpace(requested)
            ? CatalogText.Slugify(draft.Name ?? string.Empty)
            : CatalogText.Slugify(requested);
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 220)
        {
            draft.Issues.Add("El slug es obligatorio y no puede superar 220 caracteres.");
            return;
        }

        draft.Slug = slug;
        draft.SlugValid = true;
    }

    private static void ReadDescription(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "description", out var field) || field.ValueKind == JsonValueKind.Null)
        {
            draft.Description = string.Empty;
            return;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("La descripción debe ser un texto.");
            return;
        }

        draft.Description = field.GetString()?.Trim() ?? string.Empty;
    }

    private static void ReadCategory(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "category", out var field))
        {
            draft.Issues.Add("La categoría es obligatoria.");
            return;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("La categoría debe ser el slug de una categoría existente.");
            return;
        }

        var input = field.GetString()?.Trim() ?? string.Empty;
        var slug = CatalogText.Slugify(input);
        draft.CategoryInput = input;
        if (string.IsNullOrWhiteSpace(slug))
        {
            draft.Issues.Add("La categoría es obligatoria.");
            return;
        }

        draft.CategorySlug = slug;
        draft.CategoryValid = true;
    }

    private static void ReadPrice(JsonElement item, ProductImportDraft draft)
    {
        if (!TryReadDecimal(item, "price", "El precio", MaxPrice, out var price, out var error))
        {
            draft.Issues.Add(error!);
            return;
        }

        draft.Price = price;
        draft.PriceValid = true;
    }

    private static void ReadVatRate(JsonElement item, ProductImportDraft draft)
    {
        if (!TryReadDecimal(item, "vatRate", "El IVA", MaxVatRate, out var vatRate, out var error))
        {
            draft.Issues.Add(error!);
            return;
        }

        draft.VatRate = vatRate;
        draft.VatValid = true;
    }

    private static void ReadActive(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "active", out var field))
        {
            draft.Issues.Add("El campo «active» es obligatorio.");
            return;
        }

        if (field.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            draft.Issues.Add("El campo «active» debe ser verdadero o falso.");
            return;
        }

        draft.Active = field.GetBoolean();
        draft.ActiveValid = true;
    }

    private static void ReadKind(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "kind", out var field))
        {
            draft.Issues.Add("El tipo de producto es obligatorio.");
            return;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("El tipo de producto no es válido.");
            return;
        }

        draft.Kind = field.GetString() switch
        {
            nameof(ProductKind.Standard) => ProductKind.Standard,
            nameof(ProductKind.Wine) => ProductKind.Wine,
            nameof(ProductKind.Pack) => ProductKind.Pack,
            _ => null
        };
        if (draft.Kind is null)
        {
            draft.Issues.Add("El tipo de producto no es válido.");
        }
    }

    private static void ReadWine(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "wine", out var field) || field.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        draft.HasWine = true;
        if (field.ValueKind != JsonValueKind.Object)
        {
            draft.Issues.Add("Los datos de vino deben ser un objeto.");
            return;
        }

        CollectUnknownProperties(field, WineProperties, draft.Issues);
        draft.Vintage = ReadOptionalText(field, "vintage", 20, "La añada no puede superar 20 caracteres.", draft.Issues);
        draft.Grape = ReadOptionalText(field, "grape", 150, "La uva no puede superar 150 caracteres.", draft.Issues);
        if (!TryGetField(field, "alcoholPercent", out var alcohol) || alcohol.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (!TryRoundNonNegative(alcohol, MaxAlcohol, out var rounded))
        {
            draft.Issues.Add("El grado alcohólico debe estar entre 0 y 99,99.");
            return;
        }

        draft.AlcoholPercent = rounded;
    }

    private static void ReadComponents(JsonElement item, ProductImportDraft draft)
    {
        if (!TryGetField(item, "components", out var field) || field.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (field.ValueKind != JsonValueKind.Array)
        {
            draft.ComponentsTypeInvalid = true;
            draft.Issues.Add("Los componentes deben ser una lista.");
            return;
        }

        draft.HasComponents = true;
        foreach (var component in field.EnumerateArray())
        {
            ReadComponent(component, draft);
        }
    }

    private static void ReadComponent(JsonElement item, ProductImportDraft draft)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            draft.Issues.Add("Hay un componente que no es un objeto.");
            return;
        }

        CollectUnknownProperties(item, ComponentProperties, draft.Issues);
        var referenceValid = false;
        string? reference = null;
        if (!TryGetField(item, "reference", out var referenceField))
        {
            draft.Issues.Add("Hay un componente sin referencia.");
        }
        else if (referenceField.ValueKind != JsonValueKind.String)
        {
            draft.Issues.Add("La referencia de un componente debe ser un texto.");
        }
        else
        {
            reference = referenceField.GetString()?.Trim() ?? string.Empty;
            if (reference.Length is 0 or > 80)
            {
                draft.Issues.Add("La referencia de un componente es obligatoria y no puede superar 80 caracteres.");
            }
            else
            {
                referenceValid = true;
            }
        }

        var quantityValid = false;
        var quantity = 0;
        if (!TryGetField(item, "quantity", out var quantityField))
        {
            draft.Issues.Add(referenceValid
                ? $"El componente «{reference}» no tiene cantidad."
                : "Hay un componente sin cantidad.");
        }
        else if (!TryGetPositiveInt(quantityField, out quantity))
        {
            draft.Issues.Add(referenceValid
                ? $"La cantidad del componente «{reference}» debe ser un entero mayor que cero."
                : "La cantidad de un componente debe ser un entero mayor que cero.");
        }
        else
        {
            quantityValid = true;
        }

        draft.Components.Add(new ProductImportComponentDraft
        {
            Reference = referenceValid ? reference : null,
            ReferenceValid = referenceValid,
            Quantity = quantity,
            QuantityValid = quantityValid
        });
    }

    private static void ApplyKindRules(ProductImportDraft draft)
    {
        switch (draft.Kind)
        {
            case ProductKind.Standard:
                if (draft.HasWine)
                {
                    draft.Issues.Add("Un producto estándar no puede incluir datos de vino.");
                }

                if (draft.HasComponents || draft.ComponentsTypeInvalid)
                {
                    draft.Issues.Add("Un producto estándar no puede incluir componentes.");
                }

                break;
            case ProductKind.Wine:
                if (draft.HasComponents || draft.ComponentsTypeInvalid)
                {
                    draft.Issues.Add("Un vino no puede incluir componentes.");
                }

                break;
            case ProductKind.Pack:
                if (draft.HasWine)
                {
                    draft.Issues.Add("Un pack no puede incluir datos de vino.");
                }

                if (draft.ComponentsTypeInvalid)
                {
                    break;
                }

                if (!draft.HasComponents || draft.Components.Count == 0 || draft.Components.All(component => !component.IsComplete))
                {
                    draft.Issues.Add("Un pack debe incluir al menos un componente.");
                }

                break;
        }
    }

    private static void FlagDuplicateComponents(ProductImportDraft draft)
    {
        foreach (var group in draft.Components.Where(component => component.ReferenceValid).GroupBy(component => component.Reference, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                draft.Issues.Add($"El componente «{group.Key}» está duplicado.");
            }
        }
    }

    private static void FlagDuplicateReferences(List<ProductImportDraft> drafts)
    {
        foreach (var group in drafts.Where(draft => draft.ReferenceValid).GroupBy(draft => draft.Reference, StringComparer.Ordinal))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            foreach (var draft in group)
            {
                draft.Issues.Add("La referencia está duplicada en el fichero.");
            }
        }
    }

    private static void FlagDuplicateSlugs(List<ProductImportDraft> drafts)
    {
        foreach (var group in drafts.Where(draft => draft.SlugValid).GroupBy(draft => draft.Slug, StringComparer.Ordinal))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            foreach (var draft in group)
            {
                draft.Issues.Add("El slug está duplicado en el fichero.");
            }
        }
    }

    private static string? ReadOptionalText(JsonElement item, string name, int maxLength, string lengthError, List<string> issues)
    {
        if (!TryGetField(item, name, out var field) || field.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (field.ValueKind != JsonValueKind.String)
        {
            issues.Add($"El campo «{name}» debe ser un texto.");
            return null;
        }

        var text = field.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            issues.Add(lengthError);
            return null;
        }

        return text;
    }

    private static bool TryReadDecimal(
        JsonElement item,
        string name,
        string label,
        decimal maximum,
        out decimal rounded,
        out string? error)
    {
        rounded = 0;
        error = null;
        if (!TryGetField(item, name, out var field))
        {
            error = $"{label} es obligatorio.";
            return false;
        }

        if (!TryRoundNonNegative(field, maximum, out rounded))
        {
            error = field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var value) && value < 0
                ? $"{label} no puede ser negativo."
                : $"{label} no es válido para el formato del catálogo.";
            return false;
        }

        return true;
    }

    private static bool TryRoundNonNegative(JsonElement field, decimal maximum, out decimal rounded)
    {
        rounded = 0;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetDecimal(out var value) || value < 0)
        {
            return false;
        }

        rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded <= maximum;
    }

    private static bool TryGetPositiveInt(JsonElement field, out int quantity)
    {
        quantity = 0;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetDecimal(out var number))
        {
            return false;
        }

        if (number != decimal.Truncate(number) || number < 1 || number > int.MaxValue)
        {
            return false;
        }

        quantity = (int)number;
        return true;
    }

    private static bool TryGetField(JsonElement item, string name, out JsonElement field) =>
        item.TryGetProperty(name, out field);

    private static void CollectUnknownProperties(JsonElement item, HashSet<string> allowed, List<string> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                issues.Add($"La propiedad «{property.Name}» está repetida.");
            }
            else if (!allowed.Contains(property.Name))
            {
                issues.Add($"La propiedad «{property.Name}» no está permitida.");
            }
        }
    }
}
