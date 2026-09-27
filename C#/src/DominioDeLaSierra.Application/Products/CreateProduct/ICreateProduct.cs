namespace DominioDeLaSierra.Application.Products.CreateProduct;

public interface ICreateProduct
{
    Task<Guid> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default);
}
