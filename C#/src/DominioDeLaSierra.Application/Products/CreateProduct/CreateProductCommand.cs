namespace DominioDeLaSierra.Application.Products.CreateProduct;

public sealed record CreateProductCommand(
    string Reference,
    string Name,
    string? Slug,
    string Description,
    Guid CategoryId,
    decimal Price,
    decimal VatRate,
    bool Active = true);

public sealed record CreatedProductDto(
    Guid Id,
    string Reference,
    string Name,
    string Slug,
    string Description,
    Guid CategoryId,
    decimal Price,
    decimal VatRate,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
