using System.Security.Claims;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Admin;
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

        try
        {
            var username = CreateInput.Username.Trim();
            var displayName = CreateInput.DisplayName.Trim();
            if (username.Length is 0 or > 80)
            {
                throw new ArgumentException("El nombre de usuario es obligatorio y no puede superar 80 caracteres.");
            }

            if (displayName.Length is 0 or > 150)
            {
                throw new ArgumentException("El nombre visible es obligatorio y no puede superar 150 caracteres.");
            }

            if (string.IsNullOrWhiteSpace(CreateInput.Password))
            {
                throw new ArgumentException("La contraseña es obligatoria.");
            }

            if (!Enum.IsDefined(CreateInput.Role))
            {
                throw new ArgumentException("El rol no es válido.");
            }

            var passwordHash = passwordHasher.HashPassword(null!, CreateInput.Password);
            await users.CreateAsync(
                username,
                passwordHash,
                displayName,
                CreateInput.Role,
                DateTimeOffset.UtcNow,
                cancellationToken);
            StatusMessage = $"Usuario «{username}» creado.";
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

        try
        {
            if (UserId == Guid.Empty)
            {
                throw new ArgumentException("El usuario no es válido.");
            }

            if (!Enum.IsDefined(Role))
            {
                throw new ArgumentException("El rol no es válido.");
            }

            await users.ChangeRoleAsync(CurrentActorId(), UserId, Role, cancellationToken);
            StatusMessage = "Rol actualizado.";
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

        try
        {
            if (UserId == Guid.Empty)
            {
                throw new ArgumentException("El usuario no es válido.");
            }

            await users.SetActiveAsync(CurrentActorId(), UserId, Active, cancellationToken);
            StatusMessage = Active ? "Usuario activado." : "Usuario desactivado.";
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

        try
        {
            if (UserId == Guid.Empty)
            {
                throw new ArgumentException("El usuario no es válido.");
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                throw new ArgumentException("La contraseña es obligatoria.");
            }

            var passwordHash = passwordHasher.HashPassword(null!, NewPassword);
            await users.ChangePasswordHashAsync(UserId, passwordHash, cancellationToken);
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
