using System.Data;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Admin;

// TODO: cambiar el rol, desactivar un usuario o cambiar su contraseña no invalida las cookies
// ya emitidas. La cookie es autocontenida: no hay SecurityStamp ni revalidación contra
// PostgreSQL en cada request. Revocar esas sesiones exigiría una columna nueva y
// OnValidatePrincipal. No implementarlo en esta fase.
public sealed class AdminUserManagement(ApplicationDbContext dbContext) : IAdminUserManagement
{
    public async Task<IReadOnlyList<AdminUserListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AdminUsers
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .Select(user => new AdminUserListItem(
                user.Id,
                user.Username,
                user.DisplayName,
                user.Role,
                user.Active,
                user.CreatedAt,
                user.LastLoginAt))
            .ToListAsync(cancellationToken);
    }

    public async Task CreateAsync(
        string username,
        string passwordHash,
        string displayName,
        AdminRole role,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        username = RequireUsername(username);
        displayName = RequireDisplayName(displayName);
        RequirePasswordHash(passwordHash);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("El rol no es válido.", nameof(role));
        }

        var usernameTaken = await dbContext.AdminUsers
            .AnyAsync(user => user.Username == username, cancellationToken);
        if (usernameTaken)
        {
            throw new InvalidOperationException("Ese nombre de usuario ya existe.");
        }

        dbContext.AdminUsers.Add(new AdminUser(
            Guid.NewGuid(),
            username,
            passwordHash,
            displayName,
            role,
            true,
            createdAt));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new InvalidOperationException("Ese nombre de usuario ya existe.");
        }
    }

    public async Task ChangeRoleAsync(
        Guid actorId,
        Guid userId,
        AdminRole role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("El rol no es válido.", nameof(role));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockActiveAdministratorsAsync(cancellationToken);
        var user = await LoadUserForUpdateAsync(userId, cancellationToken);

        if (actorId == user.Id && user.Role == AdminRole.Administrator && role != AdminRole.Administrator)
        {
            throw new InvalidOperationException("No puedes cambiar tu propio rol.");
        }

        if (user.Role == AdminRole.Administrator && user.Active && role != AdminRole.Administrator)
        {
            await EnsureAnotherActiveAdministratorRemainsAsync(user.Id, cancellationToken);
        }

        user.ChangeRole(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetActiveAsync(
        Guid actorId,
        Guid userId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockActiveAdministratorsAsync(cancellationToken);
        var user = await LoadUserForUpdateAsync(userId, cancellationToken);

        if (!active && actorId == user.Id)
        {
            throw new InvalidOperationException("No puedes desactivar tu propia cuenta.");
        }

        if (!active && user.Role == AdminRole.Administrator && user.Active)
        {
            await EnsureAnotherActiveAdministratorRemainsAsync(user.Id, cancellationToken);
        }

        if (active)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        RequirePasswordHash(passwordHash);
        var user = await dbContext.AdminUsers
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        user.ChangePasswordHash(passwordHash);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AdminUser> LoadUserForUpdateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = CurrentDbTransaction();
            command.CommandText = """SELECT "Id" FROM "AdminUsers" WHERE "Id" = @id FOR UPDATE""";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = userId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("El usuario no existe.");
            }
        }

        var user = await dbContext.AdminUsers
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException("El usuario no existe.");
        }

        return user;
    }

    private async Task LockActiveAdministratorsAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = CurrentDbTransaction();
        command.CommandText = """
            SELECT "Id"
            FROM "AdminUsers"
            WHERE "Role" = 'Administrator' AND "Active" = TRUE
            ORDER BY "Id"
            FOR UPDATE
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private async Task EnsureAnotherActiveAdministratorRemainsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var others = await dbContext.AdminUsers.CountAsync(
            user => user.Role == AdminRole.Administrator && user.Active && user.Id != userId,
            cancellationToken);
        if (others == 0)
        {
            throw new InvalidOperationException("Debe quedar al menos un Administrator activo.");
        }
    }

    private System.Data.Common.DbTransaction CurrentDbTransaction()
    {
        return dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("La comprobación de administradores activos requiere una transacción.");
    }

    private static string RequireUsername(string username)
    {
        username = username.Trim();
        if (username.Length is 0 or > 80)
        {
            throw new ArgumentException("El nombre de usuario es obligatorio y no puede superar 80 caracteres.", nameof(username));
        }

        return username;
    }

    private static string RequireDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        if (displayName.Length is 0 or > 150)
        {
            throw new ArgumentException("El nombre visible es obligatorio y no puede superar 150 caracteres.", nameof(displayName));
        }

        return displayName;
    }

    private static void RequirePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 500)
        {
            throw new ArgumentException("El hash de la contraseña no es válido.", nameof(passwordHash));
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
