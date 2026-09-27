using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.GetProducts;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(IProductCatalogQueries productCatalogQueries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductListItemDto>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetProductsQuery.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = GetProductsQuery.Create(page, pageSize, search, category, minPrice, maxPrice);
            var result = await productCatalogQueries.GetProductsAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
