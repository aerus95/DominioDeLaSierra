using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(
    ICategoryCatalogQueries categoryCatalogQueries,
    ICreateCategory createCategory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryListItemDto>>> Get(
        CancellationToken cancellationToken)
    {
        var categories = await categoryCatalogQueries.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    // TODO: Proteger este endpoint con autenticación y autorización antes de exponerlo en producción pública.
    [HttpPost]
    public async Task<ActionResult<CreatedCategoryDto>> Post(
        [FromBody] CreateCategoryRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "El cuerpo de la petición es obligatorio." });
        }

        try
        {
            var created = await createCategory.ExecuteAsync(
                new CreateCategoryCommand(
                    request.Name ?? string.Empty,
                    request.Slug,
                    request.ParentCategoryId,
                    request.Active),
                cancellationToken);

            return CreatedAtAction(nameof(Get), created);
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

public sealed record CreateCategoryRequest(
    string? Name,
    string? Slug,
    Guid? ParentCategoryId,
    bool Active = true);
