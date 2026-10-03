namespace DominioDeLaSierra.Application.Products.ImportProducts;

public interface IImportProducts
{
    Task<ProductImportReport> ExecuteAsync(
        ImportProductsCommand command,
        CancellationToken cancellationToken = default);
}
