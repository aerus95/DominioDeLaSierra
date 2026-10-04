using System.Security.Claims;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin;

[AllowAnonymous]
public sealed class LoginModel(AdminCredentialVerifier credentials) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet(string? returnUrl)
    {
        ReturnUrl = LocalReturnUrl(returnUrl);
        if (User.Identity?.IsAuthenticated == true && HasPanelRole(User))
        {
            return LocalRedirect(ReturnUrl);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var returnUrl = LocalReturnUrl(ReturnUrl);
        if (AdminPostedValues.HasBindingError(ModelState, nameof(Username))
            || AdminPostedValues.HasBindingError(ModelState, nameof(Password)))
        {
            ErrorMessage = "Usuario o contraseña incorrectos.";
            ReturnUrl = returnUrl;
            return Page();
        }

        var user = await credentials.AuthenticateAsync(Username, Password, cancellationToken);
        if (user is null)
        {
            ErrorMessage = "Usuario o contraseña incorrectos.";
            ReturnUrl = returnUrl;
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, AdminAuthOptions.Scheme);
        await HttpContext.SignInAsync(AdminAuthOptions.Scheme, new ClaimsPrincipal(identity));
        return LocalRedirect(returnUrl);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(AdminAuthOptions.Scheme);
        return Redirect("/admin/login");
    }

    private string LocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            return "/admin";
        }

        return returnUrl;
    }

    private static bool HasPanelRole(ClaimsPrincipal user)
    {
        return user.IsInRole(nameof(AdminRole.Administrator))
            || user.IsInRole(nameof(AdminRole.Manager))
            || user.IsInRole(nameof(AdminRole.Viewer));
    }
}
