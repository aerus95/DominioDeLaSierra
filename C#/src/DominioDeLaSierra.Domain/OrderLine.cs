namespace DominioDeLaSierra.Domain;

public sealed record OrderLine(
    Guid ProductId,
    string Name,
    string Reference,
    ProductKind Kind,
    int Quantity,
    long UnitPriceCents,
    decimal VatRate,
    IReadOnlyList<OrderLineComponent>? Components);

public sealed record OrderLineComponent(
    Guid ComponentProductId,
    string Name,
    string Reference,
    int Quantity);
