namespace DominioDeLaSierra.Application.Products.ImportProducts;

public sealed record ImportProductsCommand(Stream Content, long Length);
