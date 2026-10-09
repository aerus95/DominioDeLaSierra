using DominioDeLaSierra.Application.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[EnableRateLimiting("checkout")]
[RequestSizeLimit(16 * 1024)]
[Route("api/v1/checkout")]
public sealed class CheckoutController(ICheckout checkout, IOrderPaymentSessions sessions) : ControllerBase
{
    [HttpPost("quotes")]
    public async Task<ActionResult<CheckoutQuoteDto>> Quote(
        [FromBody] CheckoutQuoteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            return Ok(await checkout.QuoteAsync(request, cancellationToken));
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

    [HttpPost("orders")]
    public async Task<ActionResult<GuestOrderDto>> Place(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] PlaceGuestOrderRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            var placement = await checkout.PlaceGuestOrderAsync(idempotencyKey, request, cancellationToken);
            return placement.Created
                ? StatusCode(StatusCodes.Status201Created, placement.Order)
                : Ok(placement.Order);
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

    [HttpPost("payments")]
    public async Task<ActionResult<CheckoutPaymentDto>> Pay(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] PlaceGuestOrderRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            return Ok(await sessions.PayAsync(idempotencyKey, request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (StripeCheckoutException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    [HttpPost("payment-sessions")]
    public async Task<ActionResult<CheckoutSessionDto>> StartPayment(
        [FromHeader(Name = "Checkout-Access-Token")] string? checkoutAccessToken,
        [FromBody] StartCheckoutSessionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            return Ok(await sessions.StartAsync(request.OrderId, checkoutAccessToken, cancellationToken));
        }
        catch (CheckoutNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (StripeCheckoutException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }
}
