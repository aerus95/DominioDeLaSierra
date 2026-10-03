using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Admin;

public sealed record AdminUserListItem(
    Guid Id,
    string Username,
    string DisplayName,
    AdminRole Role,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public interface IAdminUserManagement
{
    Task<IReadOnlyList<AdminUserListItem>> ListAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(
        string username,
        string passwordHash,
        string displayName,
        AdminRole role,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task ChangeRoleAsync(
        Guid actorId,
        Guid userId,
        AdminRole role,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        Guid actorId,
        Guid userId,
        bool active,
        CancellationToken cancellationToken = default);

    Task ChangePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
