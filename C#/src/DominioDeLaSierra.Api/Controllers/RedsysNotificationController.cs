using DominioDeLaSierra.Application.Checkout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DominioDeLaSierra.Api.Controllers;

[ApiController]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("redsys-notification")]
[RequestSizeLimit(16 * 1024)]
[Route("api/v1/checkout/redsys")]
public sealed class RedsysNotificationController(
    IRedsysNotifications notifications,
    ILogger<RedsysNotificationController> logger) : ControllerBase
{
    [HttpPost("notification")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest(new { error = RedsysSignature.InvalidSignatureMessage });
        }

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            await notifications.ReceiveAsync(
                form["Ds_SignatureVersion"],
                form["Ds_MerchantParameters"],
                form["Ds_Signature"],
                cancellationToken);
            return Ok();
        }
        catch (RedsysSignatureException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "La notificación de Redsys no se ha podido registrar. Tipo {ExceptionType}.",
                exception.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = RedsysNotificationRules.UnavailableMessage });
        }
    }
}
