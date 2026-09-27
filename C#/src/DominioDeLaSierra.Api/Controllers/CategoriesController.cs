using DominioDeLaSierra.Application.Categories.GetCategories;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(ICategoryCatalogQueries categoryCatalogQueries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryListItemDto>>> Get(
        CancellationToken cancellationToken)
    {
        var categories = await categoryCatalogQueries.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }
}
