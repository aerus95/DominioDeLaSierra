namespace DominioDeLaSierra.Application.Products.ImportProducts;

public static class ProductImportLimits
{
    public const int MaxJsonBytes = 1_048_576;
    public const int MaxProducts = 500;
}

public sealed record ProductImportProductErrors(string Label, IReadOnlyList<string> Messages);

public sealed record ProductImportCounts(int Total, int Standard, int Wine, int Pack);

public sealed record ProductImportReport(
    bool IsValid,
    bool Imported,
    IReadOnlyList<string> DocumentErrors,
    IReadOnlyList<ProductImportProductErrors> Products,
    ProductImportCounts? Counts)
{
    public static ProductImportReport Document(string error) =>
        new(false, false, [error], [], null);
}
