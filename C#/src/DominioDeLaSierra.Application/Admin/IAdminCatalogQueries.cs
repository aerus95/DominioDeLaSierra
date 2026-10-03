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
    ProductKind Kind);

public interface IAdminCatalogQueries
{
    Task<IReadOnlyList<AdminCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
}
