using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DominioDeLaSierra.Api.Errors;

public sealed class NotFoundResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Features.Get<IStatusCodeReExecuteFeature>() is not null)
        {
            await next(context);
            return;
        }

        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || context.Response.HasStarted
            || context.Response.ContentLength is > 0
            || context.Response.ContentType is not null)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status404NotFound;

        var path = context.Request.Path;
        if (path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status404NotFound,
                        Title = "No se ha encontrado el recurso."
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                context.RequestAborted);
            return;
        }

        if (path.StartsWithSegments("/media") || path.StartsWithSegments("/swagger"))
        {
            return;
        }

        var originalPath = context.Request.Path;
        var originalQuery = context.Request.QueryString;
        context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPath = originalPath.Value ?? string.Empty,
            OriginalQueryString = originalQuery.HasValue ? originalQuery.Value : null
        });
        context.Request.Path = "/error/404";
        context.Request.QueryString = QueryString.Empty;
        context.SetEndpoint(null);
        context.Items.Remove("__RequestUnhandled");
        context.Features.Get<IRouteValuesFeature>()?.RouteValues.Clear();
        try
        {
            await next(context);
        }
        finally
        {
            context.Request.Path = originalPath;
            context.Request.QueryString = originalQuery;
        }
    }
}
