namespace DominioDeLaSierra.Application.Products.ImportProducts;

public sealed record ValidateProductImportCommand(Stream Content, long Length);
