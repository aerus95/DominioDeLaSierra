using DominioDeLaSierra.Infrastructure.Persistence;

namespace DominioDeLaSierra.UnitTests;

public class DatabaseGuardTests
{
    [Theory]
    [InlineData("dominio_sierra", "localhost")]
    [InlineData("dominio_sierra_checkout", "localhost")]
    [InlineData("dominio_sierra_tests_extra", "localhost")]
    [InlineData("dominio_sierra_tests", "db.example.com")]
    public void Destructive_targets_abort_before_any_statement(string databaseName, string host)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseGuard.RequireAutomatedTestDatabase(databaseName, host));

        Assert.Contains("No se ha ejecutado ninguna operación.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Accepts_only_the_local_automated_test_database(string host)
    {
        var name = DatabaseGuard.RequireAutomatedTestDatabase("dominio_sierra_tests", host);

        Assert.Equal("dominio_sierra_tests", name);
        DatabaseGuard.EnsureAutomatedTestConnection(Connection("dominio_sierra_tests", host));
    }

    [Fact]
    public void Rejects_a_connection_whose_name_only_shares_the_test_prefix()
    {
        var connection = Connection("dominio_sierra_tests_extra", "localhost");

        Assert.Contains("Database=dominio_sierra_tests", connection, StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseGuard.EnsureAutomatedTestConnection(connection));

        Assert.Contains("No se ha ejecutado ninguna operación.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dominio_sierra")]
    [InlineData("dominio_sierra_checkout")]
    [InlineData("dominio_sierra_tests_extra")]
    public void Testing_rejects_any_database_other_than_the_automated_one(string databaseName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseGuard.EnsureProcess("Testing", Connection(databaseName, "localhost")));

        Assert.Contains("No se ha ejecutado ninguna operación.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_rejects_the_automated_database_when_the_host_is_remote()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseGuard.EnsureProcess("Testing", Connection("dominio_sierra_tests", "db.example.com")));

        Assert.Contains("No se ha ejecutado ninguna operación.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_accepts_the_local_automated_database()
    {
        DatabaseGuard.EnsureProcess("Testing", Connection("dominio_sierra_tests", "localhost"));
    }

    [Fact]
    public void Development_rejects_the_automated_test_database()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseGuard.EnsureProcess("Development", Connection("dominio_sierra_tests", "localhost")));

        Assert.Contains("No se ha ejecutado ninguna operación.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dominio_sierra")]
    [InlineData("dominio_sierra_checkout")]
    public void Development_can_use_its_own_local_databases(string databaseName)
    {
        DatabaseGuard.EnsureProcess("Development", Connection(databaseName, "localhost"));
    }

    [Fact]
    public void Drop_and_exists_statements_name_only_the_automated_test_database()
    {
        Assert.Equal(
            "DROP DATABASE IF EXISTS dominio_sierra_tests WITH (FORCE);",
            DatabaseGuard.AutomatedTestDropStatement());
        Assert.Equal(
            "SELECT 1 FROM pg_database WHERE datname = 'dominio_sierra_tests'",
            DatabaseGuard.AutomatedTestExistsQuery());
    }

    private static string Connection(string databaseName, string host) =>
        $"Host={host};Port=5432;Database={databaseName};Username=dominio_sierra_app;Password=not-used";
}
