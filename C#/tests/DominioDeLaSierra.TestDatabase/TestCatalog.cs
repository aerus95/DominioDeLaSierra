using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DominioDeLaSierra.TestDatabase;

public static class TestCatalog
{
    public static async Task PrepareAsync()
    {
        await EnsureAsync();
        await ResetAsync();
        await SeedAsync();
        WriteImageFixtures();
    }

    public static async Task EnsureAsync()
    {
        TestAccounts.EnsureSafeDatabase(TestAccounts.DatabaseName, "localhost");
        await CreateDatabaseIfNeededAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public static async Task ResetAsync()
    {
        TestAccounts.EnsureSafeDatabase(TestAccounts.DatabaseName, "localhost");
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "StockMovements",
                "Stocks",
                "ProductComponents",
                "Wines",
                "Products",
                "Categories",
                "AdminUsers"
            RESTART IDENTITY CASCADE;
            """);
    }

    public static async Task SeedAsync()
    {
        TestAccounts.EnsureSafeDatabase(TestAccounts.DatabaseName, "localhost");
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var hasher = new PasswordHasher<AdminUserAuth>();
        db.AdminUsers.AddRange(
            User(hasher, TestAccounts.AdminUsername, "E2E Admin", AdminRole.Administrator, now),
            User(hasher, TestAccounts.ManagerUsername, "E2E Manager", AdminRole.Manager, now),
            User(hasher, TestAccounts.ViewerUsername, "E2E Viewer", AdminRole.Viewer, now));
        db.Categories.Add(new Category(
            Guid.NewGuid(),
            TestAccounts.CategoryName,
            TestAccounts.CategorySlug,
            null,
            true));
        await db.SaveChangesAsync();
    }

    public static async Task CleanupAsync()
    {
        await EnsureAsync();
        await ResetAsync();
    }

    public static async Task DropAsync()
    {
        TestAccounts.EnsureSafeDatabase(TestAccounts.DatabaseName, "localhost");
        await RunDockerAsync(
            "psql",
            "-U",
            "dominio_sierra_app",
            "-d",
            "dominio_sierra",
            "-v",
            "ON_ERROR_STOP=1",
            "-c",
            $"DROP DATABASE IF EXISTS {TestAccounts.DatabaseName} WITH (FORCE);");
    }

    public static string FixtureDirectory()
    {
        var root = FindRepoRoot();
        return Path.Combine(root, "e2e", "playwright", "fixtures");
    }

    private static void WriteImageFixtures()
    {
        var directory = FixtureDirectory();
        Directory.CreateDirectory(directory);
        WritePng(Path.Combine(directory, "valid-800.png"), 800, 800, new Rgba32(120, 40, 48));
        WritePng(Path.Combine(directory, "tiny-100.png"), 100, 100, new Rgba32(40, 40, 40));
        File.WriteAllBytes(Path.Combine(directory, "fake.jpg"), [0xFF, 0xD8, 0xFF, 0x00, 0x11, 0x22, 0x33]);
    }

    private static void WritePng(string path, int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.Save(path, new PngEncoder());
    }

    private static AdminUser User(
        PasswordHasher<AdminUserAuth> hasher,
        string username,
        string displayName,
        AdminRole role,
        DateTimeOffset createdAt)
    {
        return new AdminUser(
            Guid.NewGuid(),
            username,
            hasher.HashPassword(new AdminUserAuth(Guid.Empty, username, "unused", displayName, role), TestAccounts.Password),
            displayName,
            role,
            true,
            createdAt);
    }

    private static ApplicationDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "localhost",
                ["Database:Port"] = "5432",
                ["Database:Name"] = TestAccounts.DatabaseName,
                ["Database:Username"] = "dominio_sierra_app"
            })
            .Build();
        var connectionString = PostgresConnection.Build(configuration);
        if (!connectionString.Contains($"Database={TestAccounts.DatabaseName}", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La cadena de conexión de pruebas no apunta a dominio_sierra_tests.");
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task CreateDatabaseIfNeededAsync()
    {
        var existing = await RunDockerAsync(
            "psql",
            "-U",
            "dominio_sierra_app",
            "-d",
            "dominio_sierra",
            "-tAc",
            $"SELECT 1 FROM pg_database WHERE datname = '{TestAccounts.DatabaseName}'");
        if (existing.Contains('1', StringComparison.Ordinal))
        {
            return;
        }

        await RunDockerAsync(
            "createdb",
            "-U",
            "dominio_sierra_app",
            TestAccounts.DatabaseName);
    }

    private static async Task<string> RunDockerAsync(string command, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("dominio-sierra-postgres-local");
        start.ArgumentList.Add(command);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("No se ha podido ejecutar docker.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"No se ha podido preparar la base de pruebas (docker exit {process.ExitCode}). {error}");
        }

        return output;
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "C#"))
                    && Directory.Exists(Path.Combine(current.FullName, "e2e")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("No se ha encontrado la raíz del repositorio para los assets de prueba.");
    }
}
