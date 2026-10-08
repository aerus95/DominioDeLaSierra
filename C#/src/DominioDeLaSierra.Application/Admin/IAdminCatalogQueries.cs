using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Admin;

public sealed record AdminCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    bool Active);

public sealed record AdminProductDto(
    Guid Id,
    string Reference,
    string Name,
    string Slug,
    string Description,
    decimal Price,
    decimal VatRate,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string? PrimaryImageUrl,
    ProductKind Kind,
    int? StockQuantity);

public sealed record AdminEditorComponentDto(Guid ProductId, int Quantity);

public sealed record AdminEditorCandidateDto(Guid ProductId, string Label);

public sealed record AdminComponentCandidateDto(
    Guid Id,
    string Reference,
    string Name,
    string Kind,
    bool Active);

public sealed record AdminProductListQuery(
    string? Search,
    Guid? CategoryId,
    ProductKind? Kind,
    bool? Active)
{
    public static AdminProductListQuery Unfiltered { get; } = new(null, null, null, null);
}

public sealed record AdminCategoryListQuery(
    string? Search,
    bool? Active,
    Guid? ParentCategoryId,
    bool RootsOnly)
{
    public static AdminCategoryListQuery Unfiltered { get; } = new(null, null, null, false);
}

public sealed record AdminListResult<T>(IReadOnlyList<T> Items, int TotalCount);

public static class AdminListSearch
{
    public const int MaxLength = 200;

    public static string? Normalize(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }
}

public sealed record AdminProductEditorDto(
    Guid Id,
    string Reference,
    string Name,
    string Slug,
    string Description,
    Guid CategoryId,
    string Price,
    string VatRate,
    bool Active,
    string Kind,
    string FichaRevision,
    int? Stock,
    string Vintage,
    string Grape,
    string Alcohol,
    IReadOnlyList<AdminEditorComponentDto> Components,
    IReadOnlyList<AdminEditorCandidateDto> Candidates);

public interface IAdminCatalogQueries
{
    Task<AdminListResult<AdminCategoryDto>> GetCategoriesAsync(
        AdminCategoryListQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminListResult<AdminProductDto>> GetProductsAsync(
        AdminProductListQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminProductEditorDto?> GetProductEditorAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminComponentCandidateDto>> GetComponentCandidatesAsync(CancellationToken cancellationToken = default);
}
