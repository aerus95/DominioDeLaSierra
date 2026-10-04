namespace DominioDeLaSierra.IntegrationTests;

[Collection(PostgresCollection.Name)]
public abstract class DatabaseTest(PostgresFixture fixture) : IAsyncLifetime
{
    protected PostgresFixture Fixture { get; } = fixture;

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
