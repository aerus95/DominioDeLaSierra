namespace DominioDeLaSierra.TestDatabase;

public static class TestAccounts
{
    public const string DatabaseName = "dominio_sierra_tests";
    public const string Password = "Test-Only-12345";
    public const string AdminUsername = "e2e-admin";
    public const string ManagerUsername = "e2e-manager";
    public const string ViewerUsername = "e2e-viewer";
    public const string CategoryName = "E2E Catalogo";
    public const string CategorySlug = "e2e-catalogo";

    public static void EnsureSafeDatabase(string databaseName, string host)
    {
        if (!databaseName.EndsWith("_tests", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La base de pruebas debe terminar en _tests. No se ha ejecutado ninguna operación.");
        }

        if (host is not ("localhost" or "127.0.0.1"))
        {
            throw new InvalidOperationException(
                "La base de pruebas solo puede estar en localhost. No se ha ejecutado ninguna operación.");
        }
    }
}
