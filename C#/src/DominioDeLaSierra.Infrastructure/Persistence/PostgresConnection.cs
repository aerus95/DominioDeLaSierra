using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Persistence;

public static class PostgresConnection
{
    public static string Build(IConfiguration configuration)
    {
        var database = configuration.GetSection("Database");
        var password = configuration["Database:Password"]
            ?? LocalEnvFile.Get("POSTGRES_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "PostgreSQL password is not configured. Set POSTGRES_PASSWORD in the environment or in the repo-root .env file.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = database["Host"] ?? "localhost",
            Port = int.TryParse(database["Port"], out var port) ? port : 5432,
            Database = database["Name"] ?? "dominio_sierra",
            Username = database["Username"] ?? "dominio_sierra_app",
            Password = password
        };

        return builder.ConnectionString;
    }
}
