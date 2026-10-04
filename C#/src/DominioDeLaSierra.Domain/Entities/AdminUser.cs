using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class AdminUser
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public AdminRole Role { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    private AdminUser()
    {
    }

    public AdminUser(
        Guid id,
        string username,
        string passwordHash,
        string displayName,
        AdminRole role,
        bool active,
        DateTimeOffset createdAt)
    {
        Id = id;
        Username = username;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Role = role;
        Active = active;
        CreatedAt = createdAt;
    }

    public void RecordLogin(DateTimeOffset at)
    {
        LastLoginAt = at;
    }

    public void ChangeRole(AdminRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("El rol no es válido.", nameof(role));
        }

        Role = role;
    }

    public void Activate()
    {
        Active = true;
    }

    public void Deactivate()
    {
        Active = false;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 500)
        {
            throw new ArgumentException("La contraseña no es válida.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }
}
