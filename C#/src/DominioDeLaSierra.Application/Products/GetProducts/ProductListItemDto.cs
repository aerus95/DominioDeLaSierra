namespace DominioDeLaSierra.Application.Products.GetProducts;

public sealed record ProductListItemDto(
    Guid Id,
    string Reference,
    string Name,
    string Slug,
    string Description,
    decimal Price,
    decimal VatRate,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug);
