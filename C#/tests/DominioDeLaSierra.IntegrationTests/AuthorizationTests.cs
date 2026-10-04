using System.Net;
using System.Text.RegularExpressions;
using DominioDeLaSierra.TestDatabase;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class AuthorizationTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Administrator_can_open_the_panel_manage_users_and_change_the_catalog()
    {
        await TestCatalog.SeedAsync();
        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);

        var panel = await client.GetAsync("/admin");
        var users = await client.GetAsync("/admin/users");
        var denied = await PostCategoryAsync(client, "TEST-admin-cat");

        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("\"ok\":true", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_can_change_the_catalog_but_not_manage_users()
    {
        await TestCatalog.SeedAsync();
        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ManagerUsername);

        var panel = await client.GetAsync("/admin");
        var users = await client.GetAsync("/admin/users");
        var write = await PostCategoryAsync(client, "TEST-manager-cat");

        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Contains("/admin", users.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_open_the_panel_but_cannot_write_or_manage_users()
    {
        await TestCatalog.SeedAsync();
        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ViewerUsername);

        var panel = await client.GetAsync("/admin");
        var users = await client.GetAsync("/admin/users");
        var write = await PostCategoryAsync(client, "TEST-viewer-cat");
        var body = await write.Content.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("No tienes permiso para modificar el catálogo.", json.RootElement.GetProperty("message").GetString());
        Assert.StartsWith("application/json", write.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
    }

    private HttpClient CreateClient()
    {
        var handler = new CookieHandler
        {
            InnerHandler = Fixture.Factory.Server.CreateHandler()
        };
        return new HttpClient(handler)
        {
            BaseAddress = Fixture.Factory.Server.BaseAddress
        };
    }

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/admin/login");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = TestAccounts.Password,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCategoryAsync(HttpClient client, string name)
    {
        var page = await client.GetAsync("/admin");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/admin?handler=CreateCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["InputCategory.Name"] = name,
            ["InputCategory.Active"] = "true",
            ["__RequestVerificationToken"] = token
        }));
    }
}
