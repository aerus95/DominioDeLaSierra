namespace DominioDeLaSierra.Application.Products.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid ProductId,
    string? FichaRevision,
    string? Reference,
    string? Name,
    string? Slug,
    string? Description,
    string? CategoryId,
    string? Price,
    string? VatRate,
    bool? Active,
    bool ActiveSpecified,
    string? LoadedStock,
    string? Stock,
    bool StockSpecified,
    string? Vintage,
    string? Grape,
    string? Alcohol,
    string? ComponentsJson,
    bool ComponentsSpecified);

public interface IUpdateProduct
{
    Task ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken = default);
}
