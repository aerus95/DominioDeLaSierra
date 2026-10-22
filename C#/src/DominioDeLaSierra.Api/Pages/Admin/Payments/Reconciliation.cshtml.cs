using System.Security.Claims;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin.Payments;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
public sealed class ReconciliationModel(IProviderPaymentReconciliation reconciliation) : PageModel
{
    public IReadOnlyList<ProviderReconciliationRow> Rows { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool CanRecheck => User.IsInRole(nameof(AdminRole.Administrator));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await reconciliation.ListPendingAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecheckAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!CanRecheck)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                Content = "No tienes permiso para comprobar PayPal.",
                ContentType = "text/plain; charset=utf-8"
            };
        }

        var actorValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(actorValue, out var actorId))
        {
            ErrorMessage = "No se ha podido identificar al usuario.";
            return RedirectToPage();
        }

        try
        {
            var result = await reconciliation.RecheckPayPalAsync(orderId, actorId, cancellationToken);
            var message = Safe(result.Message);
            if (result.Accepted)
            {
                StatusMessage = message;
            }
            else
            {
                ErrorMessage = message;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = Safe(exception.Message);
        }

        return RedirectToPage();
    }

    private static string Safe(string? reason)
    {
        if (reason is ProviderReconciliationRules.ConfirmedMessage
            or ProviderReconciliationRules.ReleasedMessage
            or ProviderReconciliationRules.KeptMessage
            or ProviderReconciliationRules.UnreachableMessage
            or ProviderReconciliationRules.NotPayPalMessage
            or ProviderReconciliationRules.NotConfiguredMessage
            or "No se ha podido identificar al usuario.")
        {
            return reason;
        }

        return "No se ha podido comprobar PayPal. La reserva se conserva.";
    }
}
