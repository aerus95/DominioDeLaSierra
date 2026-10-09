using System.Security.Claims;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin.Orders;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
public sealed class DetailModel(IAdminOrders orders) : PageModel
{
    public AdminOrderDetail? Order { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool CanWrite =>
        User.IsInRole(nameof(AdminRole.Administrator))
        || User.IsInRole(nameof(AdminRole.Manager));

    public async Task OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Order = await orders.GetAsync(id, cancellationToken);
        if (Order is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    public async Task<IActionResult> OnPostAdvanceAsync(
        Guid id,
        string? target,
        string? carrier,
        string? trackingNumber,
        CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                Content = "No tienes permiso para gestionar pedidos.",
                ContentType = "text/plain; charset=utf-8"
            };
        }

        try
        {
            if (!Enum.TryParse<FulfillmentStatus>(target, out var status) || !Enum.IsDefined(status))
            {
                throw new ArgumentException("El estado de preparación no es válido.");
            }

            var actorValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(actorValue, out var actorId))
            {
                throw new InvalidOperationException("No se ha podido identificar al usuario.");
            }

            await orders.AdvanceFulfillmentAsync(
                new AdvanceFulfillment(id, status, actorId, carrier, trackingNumber),
                cancellationToken);
            StatusMessage = "El estado de preparación se ha actualizado.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = SafeMessage(exception);
        }

        return RedirectToPage(new { id });
    }

    private static string SafeMessage(Exception exception)
    {
        if (exception.Message is "Solo se puede preparar un pedido pagado."
            or "El pedido ya está en ese estado de preparación."
            or "El pedido no puede pasar a ese estado de preparación."
            or "El estado de preparación no es válido."
            or "El usuario responsable no es válido."
            or "La fecha del cambio no es válida."
            or "El pedido no existe."
            or "No se ha podido identificar al usuario.")
        {
            return exception.Message;
        }

        if (exception is ArgumentException
            && (exception.Message.StartsWith("El transportista", StringComparison.Ordinal)
                || exception.Message.StartsWith("El número de seguimiento", StringComparison.Ordinal)))
        {
            return exception.Message;
        }

        return "No se ha podido actualizar la preparación.";
    }
}
