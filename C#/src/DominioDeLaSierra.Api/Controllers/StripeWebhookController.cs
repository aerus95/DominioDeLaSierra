using DominioDeLaSierra.Application.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("stripe-webhook")]
[RequestSizeLimit(StripeWebhookRules.MaxBodyBytes)]
[Route("api/v1/stripe")]
public sealed class StripeWebhookController(IStripeWebhooks webhooks, ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost("webhook")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        string payload;
        using (var reader = new StreamReader(Request.Body))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        try
        {
            await webhooks.ReceiveAsync(payload, Request.Headers["Stripe-Signature"], cancellationToken);
            return Ok(new { received = true });
        }
        catch (StripeWebhookSignatureException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (StripeCheckoutException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(
                "El webhook no se ha podido procesar. Tipo {ExceptionType}. Causa {InnerExceptionType}. SqlState {SqlState}. Restricción {ConstraintName}.",
                exception.GetType().Name,
                exception.InnerException?.GetType().Name,
                SafeSqlState(exception),
                SafeConstraintName(exception));
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = StripeWebhookRules.UnavailableMessage });
        }
    }

    private static string? SafeSqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name == "PostgresException"
                && current.GetType().GetProperty("SqlState")?.GetValue(current) is string state
                && state.Length == 5)
            {
                return state;
            }
        }

        return null;
    }

    private static string? SafeConstraintName(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name == "PostgresException"
                && current.GetType().GetProperty("ConstraintName")?.GetValue(current) is string name
                && name.Length <= 80
                && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.'))
            {
                return name;
            }
        }

        return null;
    }
}
