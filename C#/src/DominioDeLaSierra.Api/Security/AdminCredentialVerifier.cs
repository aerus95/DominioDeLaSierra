using DominioDeLaSierra.Application.Admin;
using Microsoft.AspNetCore.Identity;

namespace DominioDeLaSierra.Api.Security;

public sealed class AdminCredentialVerifier(IAdminUserAuthentication users)
{
    private readonly PasswordHasher<AdminUserAuth> passwordHasher = new();

    public async Task<AdminUserAuth?> AuthenticateAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await users.FindActiveByUsernameAsync(username.Trim(), cancellationToken);
        if (user is null)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result is not (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
        {
            return null;
        }

        await users.RecordLoginAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken);
        return user;
    }
}
