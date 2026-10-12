using System.Reflection;
using System.Security.Claims;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Api.Checkout;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireCheckoutAccessAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor descriptor
            && descriptor.MethodInfo.IsDefined(typeof(CheckoutProbeAttribute), inherit: true))
        {
            await next();
            return;
        }

        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (CheckoutAccessRules.Allows(configuration["Checkout:Access"], IsTester(context.HttpContext.User)))
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new { error = CheckoutAccessRules.UnavailableMessage })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    public static bool IsTester(ClaimsPrincipal user)
    {
        return user.Identity?.IsAuthenticated == true
            && user.IsInRole(nameof(AdminRole.Administrator));
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class CheckoutProbeAttribute : Attribute;
