using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Infrastructure.Products;

internal sealed class ProductImportComponentDraft
{
    public string? Reference { get; init; }
    public bool ReferenceValid { get; init; }
    public int Quantity { get; init; }
    public bool QuantityValid { get; init; }
    public Guid? ExistingProductId { get; set; }
    public bool ResolvesInsideFile { get; set; }
    public bool IsComplete => ReferenceValid && QuantityValid;
}

internal sealed class ProductImportDraft
{
    public int Row { get; init; }
    public string? Reference { get; set; }
    public bool ReferenceValid { get; set; }
    public string? Name { get; set; }
    public bool NameValid { get; set; }
    public string? Slug { get; set; }
    public bool SlugValid { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CategoryInput { get; set; }
    public string? CategorySlug { get; set; }
    public bool CategoryValid { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Price { get; set; }
    public bool PriceValid { get; set; }
    public decimal VatRate { get; set; }
    public bool VatValid { get; set; }
    public bool Active { get; set; }
    public bool ActiveValid { get; set; }
    public ProductKind? Kind { get; set; }
    public bool HasWine { get; set; }
    public string? Vintage { get; set; }
    public string? Grape { get; set; }
    public decimal? AlcoholPercent { get; set; }
    public bool HasComponents { get; set; }
    public bool ComponentsTypeInvalid { get; set; }
    public List<ProductImportComponentDraft> Components { get; } = [];
    public List<string> Issues { get; } = [];
    public Guid NewId { get; set; }

    public string Label =>
        ReferenceValid ? $"Producto [{Reference}]" : $"Producto [fila {Row}]";
}

internal sealed class ProductImportReadResult
{
    public bool Stop { get; set; }
    public List<string> DocumentErrors { get; } = [];
    public List<ProductImportDraft> Drafts { get; } = [];
}

internal sealed record ExistingImportedProduct(Guid Id, string Reference, string Slug, ProductKind Kind);

internal sealed class ImportBatchProduct
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public decimal Price { get; init; }
    public decimal VatRate { get; init; }
    public bool Active { get; init; }
    public ProductKind Kind { get; init; }
    public string? Vintage { get; init; }
    public string? Grape { get; init; }
    public decimal? AlcoholPercent { get; init; }
    public List<(Guid ProductId, int Quantity)> Components { get; } = [];
}
