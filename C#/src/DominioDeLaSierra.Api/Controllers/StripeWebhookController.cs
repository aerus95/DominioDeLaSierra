using DominioDeLaSierra.Application.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("stripe-webhook")]
[RequestSizeLimit(StripeWebhookRules.MaxBodyBytes)]
[Route("api/v1/stripe")]
public sealed class StripeWebhookController(IStripeWebhooks webhooks) : ControllerBase
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
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = StripeWebhookRules.UnavailableMessage });
        }
    }
}
