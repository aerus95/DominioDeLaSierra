using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Admin;

public sealed class AdminUserAuthentication(ApplicationDbContext dbContext) : IAdminUserAuthentication
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.AdminUsers.AnyAsync(cancellationToken);
    }

    public async Task<AdminUserAuth?> FindActiveByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Username == username && item.Active, cancellationToken);

        if (user is null)
        {
            return null;
        }

        return new AdminUserAuth(user.Id, user.Username, user.PasswordHash, user.DisplayName, user.Role);
    }

    public async Task RecordLoginAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.AdminUsers
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return;
        }

        user.RecordLogin(at);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CreateInitialAdministratorAsync(
        string username,
        string passwordHash,
        string displayName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.AdminUsers.AnyAsync(cancellationToken))
        {
            return false;
        }

        dbContext.AdminUsers.Add(new AdminUser(
            Guid.NewGuid(),
            username.Trim(),
            passwordHash,
            displayName.Trim(),
            AdminRole.Administrator,
            true,
            createdAt));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
