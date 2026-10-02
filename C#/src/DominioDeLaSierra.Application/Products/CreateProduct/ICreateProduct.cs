namespace DominioDeLaSierra.Application.Products.CreateProduct;

public interface ICreateProduct
{
    Task<CreatedProductDto> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default);
}
