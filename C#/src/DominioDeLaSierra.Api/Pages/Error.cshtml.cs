using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages;

public sealed class ErrorModel : PageModel
{
    public int Status { get; private set; }

    public string Title { get; private set; } = "Página no encontrada";

    public string Message { get; private set; } = "No hemos encontrado la página que buscas.";

    public bool OfferPanel { get; private set; }

    public IActionResult OnGet(int? code)
    {
        var statusFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var originalPath = statusFeature?.OriginalPath
            ?? exceptionFeature?.Path
            ?? string.Empty;

        if (exceptionFeature is not null)
        {
            Status = StatusCodes.Status500InternalServerError;
        }
        else if (statusFeature is not null)
        {
            Status = code is >= 400 and <= 599 ? code.Value : StatusCodes.Status404NotFound;
        }
        else
        {
            Status = StatusCodes.Status404NotFound;
        }

        if (originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = Status;
            return new JsonResult(new ProblemDetails
            {
                Status = Status,
                Title = Status == StatusCodes.Status500InternalServerError
                    ? "Se ha producido un error inesperado."
                    : "No se ha encontrado el recurso."
            })
            {
                StatusCode = Status,
                ContentType = "application/problem+json"
            };
        }

        if (originalPath.StartsWith("/media", StringComparison.OrdinalIgnoreCase)
            || originalPath.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(Status);
        }

        if (Status == StatusCodes.Status500InternalServerError)
        {
            Title = "Algo ha ido mal";
            Message = "No hemos podido completar la solicitud. Inténtalo de nuevo dentro de unos instantes.";
        }

        OfferPanel = originalPath.StartsWith("/admin", StringComparison.OrdinalIgnoreCase);
        Response.StatusCode = Status;
        return Page();
    }
}
