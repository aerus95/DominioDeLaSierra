namespace DominioDeLaSierra.Application.Products.ImportProducts;

public interface IValidateProductImport
{
    Task<ProductImportReport> ExecuteAsync(
        ValidateProductImportCommand command,
        CancellationToken cancellationToken = default);
}
