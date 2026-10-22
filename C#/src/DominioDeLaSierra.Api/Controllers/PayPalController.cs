using DominioDeLaSierra.Api.Checkout;
using DominioDeLaSierra.Application.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[Route("api/v1/checkout/paypal")]
public sealed class PayPalController(
    IPayPalCaptures captures,
    IPayPalWebhooks webhooks,
    ILogger<PayPalController> logger) : ControllerBase
{
    [HttpPost("capture")]
    [RequireCheckoutAccess]
    [EnableRateLimiting("checkout")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<CheckoutOutcomeDto>> Capture(
        [FromHeader(Name = "Checkout-Access-Token")] string? checkoutAccessToken,
        [FromBody] PayPalCaptureRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            return Ok(await captures.CaptureAsync(request.OrderId, checkoutAccessToken, cancellationToken));
        }
        catch (CheckoutNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (PayPalTransportException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("paypal-webhook")]
    [RequestSizeLimit(PayPalRules.MaxWebhookBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var buffer = new byte[PayPalRules.MaxWebhookBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await Request.Body.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read > PayPalRules.MaxWebhookBytes)
        {
            return BadRequest(new { error = PayPalRules.SignatureMessage });
        }

        try
        {
            await webhooks.ReceiveAsync(
                new PayPalTransmission(
                    Request.Headers["paypal-transmission-id"],
                    Request.Headers["paypal-transmission-time"],
                    Request.Headers["paypal-transmission-sig"],
                    Request.Headers["paypal-cert-url"],
                    Request.Headers["paypal-auth-algo"]),
                buffer.AsMemory(0, read),
                cancellationToken);
            return Ok(new { received = true });
        }
        catch (PayPalSignatureException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("El webhook de PayPal no se ha podido registrar. Tipo {ExceptionType}.", exception.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = PayPalRules.TransportMessage });
        }
    }
}
