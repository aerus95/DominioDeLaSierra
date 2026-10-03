namespace DominioDeLaSierra.Application.Products.SetPrimaryImage;

public sealed record SetProductPrimaryImageCommand(
    Guid ProductId,
    Stream Content,
    long Length);
