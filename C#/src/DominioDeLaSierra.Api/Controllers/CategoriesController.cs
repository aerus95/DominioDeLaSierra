using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.DeleteCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Categories.UpdateCategory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(
    ICategoryCatalogQueries categoryCatalogQueries,
    ICreateCategory createCategory,
    IUpdateCategory updateCategory,
    IDeleteCategory deleteCategory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryListItemDto>>> Get(
        CancellationToken cancellationToken)
    {
        var categories = await categoryCatalogQueries.GetCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [Authorize(Policy = AdminAuthOptions.WritePolicy)]
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

    [Authorize(Policy = AdminAuthOptions.WritePolicy)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UpdatedCategoryDto>> Put(
        Guid id,
        [FromBody] UpdateCategoryRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "El cuerpo de la petición es obligatorio." });
        }

        if (request.Active is not bool active)
        {
            return BadRequest(new { error = "El estado de la categoría es obligatorio." });
        }

        try
        {
            var updated = await updateCategory.ExecuteAsync(
                new UpdateCategoryCommand(
                    id,
                    request.Name ?? string.Empty,
                    request.Slug,
                    request.ParentCategoryId,
                    active),
                cancellationToken);
            return Ok(updated);
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

    [Authorize(Policy = AdminAuthOptions.WritePolicy)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await deleteCategory.ExecuteAsync(new DeleteCategoryCommand(id), cancellationToken);
            return NoContent();
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

public sealed record UpdateCategoryRequest(
    string? Name,
    string? Slug,
    Guid? ParentCategoryId,
    bool? Active);
