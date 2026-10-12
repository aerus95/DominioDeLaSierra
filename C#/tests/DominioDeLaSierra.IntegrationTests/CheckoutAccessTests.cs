using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class CheckoutAccessTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Anonymous_calls_cannot_quote_reserve_or_open_payment_and_a_signed_webhook_still_arrives()
    {
        await using var factory = CreateFactory(CheckoutAccessRules.Testers);
        var harness = new CatalogHarness(factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ACCESS-WINE", ProductKind.Wine, initialStock: 8, vintage: "2024");
        var before = await SnapshotAsync(factory, wine.Id);
        using var anonymous = CreateClient(factory);

        var availability = await anonymous.GetFromJsonAsync<CheckoutAvailabilityDto>("/api/v1/checkout/availability");
        var quote = await anonymous.PostAsJsonAsync("/api/v1/checkout/quotes", OrderBody(wine.Id));
        var order = await PlaceAsync(anonymous, wine.Id);
        var payment = await PayAsync(anonymous, wine.Id);
        var after = await SnapshotAsync(factory, wine.Id);

        Assert.False(availability!.Allowed);
        Assert.Equal(HttpStatusCode.Forbidden, quote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, order.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, payment.StatusCode);
        Assert.Equal(CheckoutAccessRules.UnavailableMessage, await ErrorAsync(order));
        Assert.Equal(before, after);

        var (body, header) = StripeWebhookSignatures.Sign(StripeWebhookSignatures.Event(
            "evt_test_access",
            "checkout.session.completed",
            "cs_test_access_unknown",
            1_890));
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
        {
            Content = new StringContent(body)
        };
        webhook.Headers.TryAddWithoutValidation("Stripe-Signature", header);
        var accepted = await anonymous.SendAsync(webhook);
        using var rejected = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
        {
            Content = new StringContent(body)
        };
        var unsigned = await anonymous.SendAsync(rejected);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
        Assert.Equal(before, await SnapshotAsync(factory, wine.Id));
    }

    [Fact]
    public async Task A_signed_in_tester_can_reserve_stock_and_a_stranger_cannot_read_the_order()
    {
        await using var factory = CreateFactory(CheckoutAccessRules.Testers);
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ACCESS-TESTER", ProductKind.Wine, initialStock: 8, vintage: "2024");
        using var tester = CreateClient(factory);
        using var stranger = CreateClient(factory);
        await LoginAsync(tester, TestAccounts.AdminUsername);

        var availability = await tester.GetFromJsonAsync<CheckoutAvailabilityDto>("/api/v1/checkout/availability");
        var placed = await PlaceAsync(tester, wine.Id);
        var order = (await placed.Content.ReadFromJsonAsync<GuestOrderDto>())!;
        var stored = await SnapshotAsync(factory, wine.Id);
        using var outcome = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/outcome")
        {
            Content = JsonContent.Create(new { orderId = order.OrderId })
        };
        outcome.Headers.TryAddWithoutValidation("Checkout-Access-Token", order.CheckoutAccessToken);
        var hidden = await stranger.SendAsync(outcome);
        var hiddenBody = await hidden.Content.ReadAsStringAsync();
        using var owned = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/outcome")
        {
            Content = JsonContent.Create(new { orderId = order.OrderId })
        };
        owned.Headers.TryAddWithoutValidation("Checkout-Access-Token", order.CheckoutAccessToken);
        var visible = await tester.SendAsync(owned);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/outcome")
        {
            Content = JsonContent.Create(new { orderId = order.OrderId })
        };
        foreign.Headers.TryAddWithoutValidation("Checkout-Access-Token", new string('a', 32));
        var denied = await tester.SendAsync(foreign);

        Assert.True(availability!.Allowed);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Equal(6, stored.Stock);
        Assert.Equal(1, stored.Orders);
        Assert.Equal(1, stored.Reservations);
        Assert.True(stored.Movements >= 1);
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.DoesNotContain(order.Number, hiddenBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(order.Number, (await visible.Content.ReadFromJsonAsync<CheckoutOutcomeDto>())!.Number);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Theory]
    [InlineData(TestAccounts.ManagerUsername)]
    [InlineData(TestAccounts.ViewerUsername)]
    public async Task Staff_without_administration_cannot_reserve_stock(string username)
    {
        await using var factory = CreateFactory(CheckoutAccessRules.Testers);
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ACCESS-" + username, ProductKind.Wine, initialStock: 8, vintage: "2024");
        using var client = CreateClient(factory);
        await LoginAsync(client, username);

        var availability = await client.GetFromJsonAsync<CheckoutAvailabilityDto>("/api/v1/checkout/availability");
        var placed = await PlaceAsync(client, wine.Id);
        var session = await client.PostAsJsonAsync("/api/v1/checkout/payment-sessions", new { orderId = Guid.NewGuid() });
        var stored = await SnapshotAsync(factory, wine.Id);

        Assert.False(availability!.Allowed);
        Assert.Equal(HttpStatusCode.Forbidden, placed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
        Assert.Equal(8, stored.Stock);
        Assert.Equal(0, stored.Orders);
        Assert.Equal(0, stored.Reservations);
    }

    [Fact]
    public async Task A_form_post_from_an_administrator_does_not_create_an_order()
    {
        await using var factory = CreateFactory(CheckoutAccessRules.Testers);
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ACCESS-FORM", ProductKind.Wine, initialStock: 8, vintage: "2024");
        using var client = CreateClient(factory);
        await LoginAsync(client, TestAccounts.AdminUsername);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["lines[0].productId"] = wine.Id.ToString(),
                ["lines[0].quantity"] = "1"
            })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        var stored = await SnapshotAsync(factory, wine.Id);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(8, stored.Stock);
        Assert.Equal(0, stored.Orders);
        Assert.Equal(0, stored.Reservations);
    }

    [Fact]
    public async Task Closed_access_blocks_a_signed_in_tester_before_any_reservation()
    {
        await using var factory = CreateFactory(CheckoutAccessRules.Closed);
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ACCESS-CLOSED", ProductKind.Wine, initialStock: 8, vintage: "2024");
        using var tester = CreateClient(factory);
        await LoginAsync(tester, TestAccounts.AdminUsername);

        var availability = await tester.GetFromJsonAsync<CheckoutAvailabilityDto>("/api/v1/checkout/availability");
        var placed = await PlaceAsync(tester, wine.Id);
        var stored = await SnapshotAsync(factory, wine.Id);

        Assert.False(availability!.Allowed);
        Assert.Equal(HttpStatusCode.Forbidden, placed.StatusCode);
        Assert.Equal(8, stored.Stock);
        Assert.Equal(0, stored.Orders);
        Assert.Equal(0, stored.Reservations);
    }

    private static WebApplicationFactory<Program> CreateFactory(string access)
    {
        var media = Path.Combine(Path.GetTempPath(), "dominio-sierra-tests", "media-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(media);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Name", TestAccounts.DatabaseName);
            builder.UseSetting("Database:Host", "localhost");
            builder.UseSetting("Checkout:Access", access);
            builder.UseSetting("Stripe:WebhookSecret", StripeWebhookSignatures.Secret);
            builder.UseSetting("Media:RootPath", media);
            builder.UseEnvironment("Testing");
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var handler = new CookieHandler
        {
            InnerHandler = factory.Server.CreateHandler()
        };
        return new HttpClient(handler)
        {
            BaseAddress = factory.Server.BaseAddress
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

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid productId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = JsonContent.Create(OrderBody(productId))
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(message);
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid productId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(OrderBody(productId))
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(message);
    }

    private static object OrderBody(Guid productId)
    {
        return new
        {
            lines = new[] { new { productId, quantity = 2 } },
            destination = new { postalCode = "37001", countryCode = "ES" },
            customerName = "Ana Rivas",
            email = "ana@example.com",
            phone = "+34600111222",
            addressLine = "Calle Mayor 1",
            city = "Salamanca",
            province = "Salamanca"
        };
    }

    private static async Task<InventorySnapshot> SnapshotAsync(WebApplicationFactory<Program> factory, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stock = await db.Stocks.SingleAsync(item => item.ProductId == productId);
        return new InventorySnapshot(
            await db.Orders.CountAsync(),
            stock.Quantity,
            await db.StockReservations.CountAsync(),
            await db.StockMovements.CountAsync(item => item.StockId == stock.Id));
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        return body!.Error;
    }

    private sealed record InventorySnapshot(int Orders, int Stock, int Reservations, int Movements);

    private sealed record ErrorBody(string Error);
}
