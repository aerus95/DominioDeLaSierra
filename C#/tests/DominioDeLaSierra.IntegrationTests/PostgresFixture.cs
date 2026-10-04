using DominioDeLaSierra.TestDatabase;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string mediaRoot = Path.Combine(Path.GetTempPath(), "dominio-sierra-tests", "media-" + Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await TestCatalog.EnsureAsync();
        Directory.CreateDirectory(mediaRoot);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Media:RootPath", mediaRoot);
            builder.UseEnvironment("Testing");
        });
    }

    public async Task ResetAsync() => await TestCatalog.ResetAsync();

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (Directory.Exists(mediaRoot))
        {
            Directory.Delete(mediaRoot, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
