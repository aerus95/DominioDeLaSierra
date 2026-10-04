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
    Task<IReadOnlyList<AdminCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<AdminProductEditorDto?> GetProductEditorAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminComponentCandidateDto>> GetComponentCandidatesAsync(CancellationToken cancellationToken = default);
}
