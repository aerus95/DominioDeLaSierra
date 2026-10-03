using DominioDeLaSierra.Application.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Api.Security;

public sealed class AdminBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AdminBootstrapOptions> options,
    ILogger<AdminBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IAdminUserAuthentication>();
        if (await users.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Admin bootstrap skipped because an administrative user already exists.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Username)
            || string.IsNullOrEmpty(settings.Password)
            || string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            logger.LogWarning("Admin bootstrap is enabled, but the initial administrator settings are incomplete. No user was created.");
            return;
        }

        var passwordHash = new PasswordHasher<AdminUserAuth>().HashPassword(null!, settings.Password);
        var created = await users.CreateInitialAdministratorAsync(
            settings.Username,
            passwordHash,
            settings.DisplayName,
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (created)
        {
            logger.LogInformation("Initial administrator {Username} was created.", settings.Username.Trim());
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
