using System.Security.Claims;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin.Users;

// TODO: cambiar el rol, desactivar un usuario o cambiar su contraseña no invalida las cookies
// ya emitidas. No hay SecurityStamp ni revocación de sesiones en esta fase.
[Authorize(Policy = AdminAuthOptions.UserManagementPolicy)]
public sealed class UsersModel(IAdminUserManagement users) : PageModel
{
    private readonly PasswordHasher<AdminUserAuth> passwordHasher = new();

    public IReadOnlyList<AdminUserListItem> Items { get; private set; } = [];

    [BindProperty]
    public CreateUserForm CreateInput { get; set; } = new();

    [BindProperty]
    public Guid UserId { get; set; }

    [BindProperty]
    public AdminRole Role { get; set; }

    [BindProperty]
    public bool Active { get; set; }

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (RejectUnlessAdministrator() is { } denied)
        {
            return denied;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (RejectUnlessAdministrator() is { } denied)
        {
            return denied;
        }

        if (!AdminPostedValues.TryReadRole(ModelState, "CreateInput.Role", out var role))
        {
            ErrorMessage = "El rol no es válido.";
            return RedirectToPage();
        }

        try
        {
            var username = AdminAccountRules.NormalizeUsername(CreateInput.Username);
            var displayName = AdminAccountRules.NormalizeDisplayName(CreateInput.DisplayName);
            AdminAccountRules.EnsureNewPassword(CreateInput.Password);
            var passwordHash = passwordHasher.HashPassword(null!, CreateInput.Password);
            await users.CreateAsync(
                username,
                passwordHash,
                displayName,
                role,
                DateTimeOffset.UtcNow,
                cancellationToken);
            StatusMessage = "Usuario creado correctamente.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(CancellationToken cancellationToken)
    {
        if (RejectUnlessAdministrator() is { } denied)
        {
            return denied;
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(UserId), UserId, out var userId))
        {
            ErrorMessage = "El usuario no es válido.";
            return RedirectToPage();
        }

        if (!AdminPostedValues.TryReadRole(ModelState, nameof(Role), out var role))
        {
            ErrorMessage = "El rol no es válido.";
            return RedirectToPage();
        }

        try
        {
            await users.ChangeRoleAsync(CurrentActorId(), userId, role, cancellationToken);
            StatusMessage = "Usuario actualizado correctamente.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(CancellationToken cancellationToken)
    {
        if (RejectUnlessAdministrator() is { } denied)
        {
            return denied;
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(UserId), UserId, out var userId))
        {
            ErrorMessage = "El usuario no es válido.";
            return RedirectToPage();
        }

        if (!AdminPostedValues.TryReadBool(ModelState, nameof(Active), out var active))
        {
            ErrorMessage = "No se ha podido actualizar el estado del usuario.";
            return RedirectToPage();
        }

        try
        {
            await users.SetActiveAsync(CurrentActorId(), userId, active, cancellationToken);
            StatusMessage = active ? "Usuario activado." : "Usuario desactivado.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (RejectUnlessAdministrator() is { } denied)
        {
            return denied;
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(UserId), UserId, out var userId))
        {
            ErrorMessage = "El usuario no es válido.";
            return RedirectToPage();
        }

        if (AdminPostedValues.HasBindingError(ModelState, nameof(NewPassword)))
        {
            ErrorMessage = "La contraseña no es válida.";
            return RedirectToPage();
        }

        try
        {
            AdminAccountRules.EnsureNewPassword(NewPassword);
            var passwordHash = passwordHasher.HashPassword(null!, NewPassword);
            await users.ChangePasswordHashAsync(userId, passwordHash, cancellationToken);
            StatusMessage = "Contraseña actualizada.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Items = await users.ListAsync(cancellationToken);
    }

    private IActionResult? RejectUnlessAdministrator()
    {
        if (User.IsInRole(nameof(AdminRole.Administrator)))
        {
            return null;
        }

        return Redirect("/admin");
    }

    private Guid CurrentActorId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(value, out var actorId))
        {
            throw new InvalidOperationException("La sesión no es válida.");
        }

        return actorId;
    }

    public sealed class CreateUserForm
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public AdminRole Role { get; set; } = AdminRole.Viewer;
    }
}
