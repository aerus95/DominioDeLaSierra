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
