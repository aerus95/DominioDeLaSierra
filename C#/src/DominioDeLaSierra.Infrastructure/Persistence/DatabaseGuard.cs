using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Persistence;

public static class DatabaseGuard
{
    public const string AutomatedTestDatabase = "dominio_sierra_tests";

    public static void EnsureProcess(string environmentName, string connectionString)
    {
        var builder = Read(connectionString);
        if (IsTesting(environmentName))
        {
            EnsureAutomatedTestTarget(builder.Database ?? string.Empty, builder.Host ?? string.Empty);
            return;
        }

        if (IsDevelopment(environmentName)
            && string.Equals(builder.Database, AutomatedTestDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Development no puede usar dominio_sierra_tests. No se ha ejecutado ninguna operación.");
        }
    }

    public static void EnsureAutomatedTestTarget(string databaseName, string host)
    {
        if (!string.Equals(databaseName, AutomatedTestDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La operación solo puede ejecutarse sobre dominio_sierra_tests. No se ha ejecutado ninguna operación.");
        }

        if (host is not ("localhost" or "127.0.0.1"))
        {
            throw new InvalidOperationException(
                "La base de pruebas solo puede estar en localhost. No se ha ejecutado ninguna operación.");
        }
    }

    public static void EnsureAutomatedTestConnection(string connectionString)
    {
        var builder = Read(connectionString);
        EnsureAutomatedTestTarget(builder.Database ?? string.Empty, builder.Host ?? string.Empty);
    }

    public static string RequireAutomatedTestDatabase(string databaseName, string host)
    {
        EnsureAutomatedTestTarget(databaseName, host);
        EnsureIdentifier(AutomatedTestDatabase);
        return AutomatedTestDatabase;
    }

    public static string AutomatedTestDropStatement()
    {
        var name = RequireAutomatedTestDatabase(AutomatedTestDatabase, "localhost");
        return $"DROP DATABASE IF EXISTS {name} WITH (FORCE);";
    }

    public static string AutomatedTestExistsQuery()
    {
        var name = RequireAutomatedTestDatabase(AutomatedTestDatabase, "localhost");
        return $"SELECT 1 FROM pg_database WHERE datname = '{name}'";
    }

    private static NpgsqlConnectionStringBuilder Read(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "La operación solo puede ejecutarse sobre dominio_sierra_tests. No se ha ejecutado ninguna operación.");
        }

        return new NpgsqlConnectionStringBuilder(connectionString);
    }

    private static void EnsureIdentifier(string name)
    {
        if (name.Length == 0
            || name.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new InvalidOperationException(
                "La operación solo puede ejecutarse sobre dominio_sierra_tests. No se ha ejecutado ninguna operación.");
        }
    }

    private static bool IsTesting(string environmentName) =>
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    private static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}
