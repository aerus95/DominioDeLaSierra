using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.GetProducts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(
    IProductCatalogQueries productCatalogQueries,
    ICreateProduct createProduct) : ControllerBase
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

    [HttpGet("{slug}")]
    public async Task<ActionResult<ProductListItemDto>> GetBySlug(
        string slug,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = GetProductBySlugQuery.Create(slug);
            var product = await productCatalogQueries.GetProductBySlugAsync(query, cancellationToken);
            if (product is null)
            {
                return NotFound(new { error = "Product not found." });
            }

            return Ok(product);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Policy = AdminAuthOptions.WritePolicy)]
    [HttpPost]
    public async Task<ActionResult<CreatedProductDto>> Post(
        [FromBody] CreateProductRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "El cuerpo de la petición es obligatorio." });
        }

        if (request.CategoryId is not Guid categoryId)
        {
            return BadRequest(new { error = "Selecciona una categoría." });
        }

        if (request.Price is not decimal price)
        {
            return BadRequest(new { error = "El precio es obligatorio." });
        }

        if (request.VatRate is not decimal vatRate)
        {
            return BadRequest(new { error = "El IVA es obligatorio." });
        }

        try
        {
            var created = await createProduct.ExecuteAsync(
                new CreateProductCommand(
                    request.Reference ?? string.Empty,
                    request.Name ?? string.Empty,
                    request.Slug,
                    request.Description ?? string.Empty,
                    categoryId,
                    price,
                    vatRate,
                    request.Active),
                cancellationToken);

            return CreatedAtAction(nameof(GetBySlug), new { slug = created.Slug }, created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

public sealed record CreateProductRequest(
    string? Reference,
    string? Name,
    string? Slug,
    string? Description,
    Guid? CategoryId,
    decimal? Price,
    decimal? VatRate,
    bool Active = true);
