using DominioDeLaSierra.Infrastructure.Persistence;

namespace DominioDeLaSierra.TestDatabase;

public static class TestAccounts
{
    public const string DatabaseName = DatabaseGuard.AutomatedTestDatabase;
    public const string Password = "Test-Only-12345";
    public const string AdminUsername = "e2e-admin";
    public const string ManagerUsername = "e2e-manager";
    public const string ViewerUsername = "e2e-viewer";
    public const string CategoryName = "E2E Catalogo";
    public const string CategorySlug = "e2e-catalogo";

    public static void EnsureSafeDatabase(string databaseName, string host) =>
        DatabaseGuard.EnsureAutomatedTestTarget(databaseName, host);

    public static string RequireDestructiveDatabase(string databaseName, string host) =>
        DatabaseGuard.RequireAutomatedTestDatabase(databaseName, host);
}
