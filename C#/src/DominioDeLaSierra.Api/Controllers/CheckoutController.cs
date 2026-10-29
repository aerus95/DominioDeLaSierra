using DominioDeLaSierra.Api.Checkout;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[RequireCheckoutAccess]
[EnableRateLimiting("checkout")]
[RequestSizeLimit(16 * 1024)]
[Route("api/v1/checkout")]
public sealed class CheckoutController(
    ICheckout checkout,
    IOrderPaymentSessions sessions,
    ICheckoutOutcome outcomes,
    IPaymentProviders providers,
    IConfiguration configuration,
    CheckoutCorrelation correlation) : ControllerBase
{
    [HttpGet("payment-providers")]
    public ActionResult<IReadOnlyList<PaymentProviderOfferDto>> PaymentProviders()
    {
        return Ok(providers.Configured());
    }

    [HttpGet("availability")]
    [CheckoutProbe]
    public ActionResult<CheckoutAvailabilityDto> Availability()
    {
        return Ok(new CheckoutAvailabilityDto(
            CheckoutAccessRules.Allows(configuration["Checkout:Access"], RequireCheckoutAccessAttribute.IsTester(User))));
    }

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
            if (string.Equals(request.PaymentProvider, nameof(PaymentProvider.Redsys), StringComparison.Ordinal))
            {
                Response.Headers["X-Checkout-Correlation"] = correlation.Id;
            }

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
        catch (PayPalTransportException exception)
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

    [HttpPost("outcome")]
    public async Task<ActionResult<CheckoutOutcomeDto>> Outcome(
        [FromHeader(Name = "Checkout-Access-Token")] string? checkoutAccessToken,
        [FromBody] CheckoutOutcomeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = CheckoutLimits.MissingBodyMessage });
        }

        try
        {
            return Ok(await outcomes.ReadAsync(request.OrderId, checkoutAccessToken, cancellationToken));
        }
        catch (CheckoutNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }
}
