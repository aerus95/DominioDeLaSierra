using DominioDeLaSierra.Application.Common;

namespace DominioDeLaSierra.Application.Products.GetProducts;

public interface IProductCatalogQueries
{
    Task<PagedResult<ProductListItemDto>> GetProductsAsync(
        GetProductsQuery query,
        CancellationToken cancellationToken = default);
}
