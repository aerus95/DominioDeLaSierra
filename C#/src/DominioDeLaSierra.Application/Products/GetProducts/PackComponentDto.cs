namespace DominioDeLaSierra.Application.Products.GetProducts;

public sealed record PackComponentDto(
    Guid ProductId,
    string Name,
    string Description,
    string? PrimaryImageUrl,
    int Quantity);
