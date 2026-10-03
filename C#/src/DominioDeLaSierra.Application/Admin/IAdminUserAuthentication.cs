using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Admin;

public sealed record AdminUserAuth(
    Guid Id,
    string Username,
    string PasswordHash,
    string DisplayName,
    AdminRole Role);

public interface IAdminUserAuthentication
{
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<AdminUserAuth?> FindActiveByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<bool> CreateInitialAdministratorAsync(
        string username,
        string passwordHash,
        string displayName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
