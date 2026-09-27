using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "src", "DominioDeLaSierra.Api"));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.Development.json"), optional: true)
            .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(PostgresConnection.Build(configuration));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
