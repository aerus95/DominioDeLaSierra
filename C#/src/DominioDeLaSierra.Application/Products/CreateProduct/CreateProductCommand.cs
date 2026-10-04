using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Products.CreateProduct;

public sealed record CreateProductCommand(
    string Reference,
    string Name,
    string? Slug,
    string Description,
    Guid CategoryId,
    decimal Price,
    decimal VatRate,
    bool Active = true,
    int InitialStock = 0,
    ProductKind Kind = ProductKind.Standard,
    string? Vintage = null,
    string? Grape = null,
    string? Alcohol = null,
    string? ComponentsJson = null,
    Stream? ImageContent = null,
    long ImageLength = 0);

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
