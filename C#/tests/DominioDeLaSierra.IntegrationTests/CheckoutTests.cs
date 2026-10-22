using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class CheckoutTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Quotes_shipping_at_59_99_60_00_and_60_01_euros()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "Q-WINE", ProductKind.Wine, initialStock: 20, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();

        await harness.UpdateAsync(wine.Id, draft => draft.Price = "59.99");
        var below = await QuoteAsync(client, wine.Id, 1, "37001");
        Assert.Equal(5_999, below.ProductSubtotalCents);
        Assert.Equal(600, below.ShippingTaxableBaseCents);
        Assert.Equal(21m, below.ShippingVatRate);
        Assert.Equal(126, below.ShippingVatCents);
        Assert.Equal(726, below.ShippingCents);
        Assert.False(below.ShippingVatPending);
        Assert.Equal(6_725, below.TotalCents);

        await harness.UpdateAsync(wine.Id, draft => draft.Price = "60.00");
        var exact = await QuoteAsync(client, wine.Id, 1, "28013");
        Assert.Equal(6_000, exact.ProductSubtotalCents);
        Assert.Equal(0, exact.ShippingCents);
        Assert.Equal(0, exact.ShippingTaxableBaseCents);
        Assert.Equal(0, exact.ShippingVatCents);
        Assert.Equal(21m, exact.ShippingVatRate);
        Assert.Equal(6_000, exact.TotalCents);

        await harness.UpdateAsync(wine.Id, draft => draft.Price = "60.01");
        var above = await QuoteAsync(client, wine.Id, 1, "08001");
        Assert.Equal(6_001, above.ProductSubtotalCents);
        Assert.Equal(0, above.ShippingCents);
        Assert.Equal(6_001, above.TotalCents);
    }

    [Fact]
    public async Task Ignores_prices_sent_by_the_client_and_uses_the_stored_price()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "Q-PRICE", ProductKind.Wine, initialStock: 4, vintage: "2024");
        await harness.UpdateAsync(wine.Id, draft => draft.Price = "25.50");
        using var client = Fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/checkout/quotes", new
        {
            lines = new[] { new { productId = wine.Id, quantity = 1, price = 1, vatRate = 99 } },
            destination = new { postalCode = "37001", countryCode = "ES" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteDto>())!;
        Assert.Equal(2_550, Assert.Single(quote.Lines).UnitPriceCents);
        Assert.Equal(21m, Assert.Single(quote.Lines).VatRate);
        var (taxableBase, vat) = OrderAmounts.SplitGross(2_550, 21m);
        Assert.Equal(taxableBase, quote.ProductTaxableBaseCents);
        Assert.Equal(vat, quote.ProductVatCents);
        Assert.Equal(2_550 + 726, quote.TotalCents);
        Assert.Equal(21m, quote.ShippingVatRate);
        Assert.Equal(600, quote.ShippingTaxableBaseCents);
        Assert.Equal(126, quote.ShippingVatCents);
    }

    [Fact]
    public async Task Rejects_an_inactive_product_and_a_pack_without_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var inactive = await harness.CreateProductAsync(categoryId, "Q-OFF", ProductKind.Wine, initialStock: 4, vintage: "2024");
        await harness.UpdateAsync(inactive.Id, draft => draft.Active = false);
        var empty = await harness.CreateProductAsync(categoryId, "Q-EMPTY", ProductKind.Wine, vintage: "2023");
        var pack = await harness.CreateProductAsync(categoryId, "Q-PACK", ProductKind.Pack, componentsJson: Components(empty.Id, 1));
        using var client = Fixture.Factory.CreateClient();

        var inactiveQuote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(inactive.Id, 1));
        Assert.Equal(HttpStatusCode.Conflict, inactiveQuote.StatusCode);
        Assert.Equal(OrderStockRules.UnavailableProductMessage, await ErrorAsync(inactiveQuote));

        var packQuote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(pack.Id, 1));
        Assert.Equal(HttpStatusCode.Conflict, packQuote.StatusCode);
        Assert.Equal(OrderStockRules.InsufficientStockMessage, await ErrorAsync(packQuote));

        var placed = await PlaceAsync(client, Guid.NewGuid().ToString("N"), Line(pack.Id, 1));
        Assert.Equal(HttpStatusCode.Conflict, placed.StatusCode);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task Places_a_guest_order_and_reserves_stock_without_a_payment()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ORD-GUEST", ProductKind.Wine, initialStock: 5, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();

        var response = await PlaceAsync(client, "guest-1", Line(wine.Id, 2, price: 1));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("ana@example.com", json, StringComparison.OrdinalIgnoreCase);
        var order = JsonSerializer.Deserialize<GuestOrderDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(3_780, order.ProductSubtotalCents);
        Assert.Equal(726, order.ShippingCents);
        Assert.Equal(21m, order.ShippingVatRate);
        Assert.False(order.ShippingVatPending);
        Assert.Equal(4_506, order.TotalCents);
        Assert.Equal("PendingPayment", order.Status);
        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Orders.Include(item => item.Payments).SingleAsync();
        Assert.Equal(order.OrderId, stored.Id);
        Assert.Equal(stored.CreatedAt.AddMinutes(30), stored.ReservationExpiresAt);
        Assert.Empty(stored.Payments);
    }

    [Fact]
    public async Task Sums_a_wine_and_a_pack_before_reserving()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "BOTH-W", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "BOTH-P", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        using var client = Fixture.Factory.CreateClient();

        var response = await PlaceAsync(client, "both-1", new
        {
            lines = new object[] { Line(wine.Id, 1), Line(pack.Id, 1) },
            destination = Destination("37001"),
            customerName = "Ana Rivas",
            email = "ana@example.com",
            phone = "+34600111222",
            addressLine = "Calle Mayor 1",
            city = "Salamanca",
            province = "Salamanca"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<GuestOrderDto>())!;
        Assert.Equal(3_780, order.ProductSubtotalCents);
        Assert.Equal(600, order.ShippingTaxableBaseCents);
        Assert.Equal(126, order.ShippingVatCents);
        Assert.Equal(726, order.ShippingCents);
        Assert.Equal(4_506, order.TotalCents);
        Assert.Equal(2, (await harness.StockAsync(wine.Id))!.Quantity);
    }

    [Fact]
    public async Task Gives_free_shipping_to_a_pack_and_a_bottle_at_sixty_euros()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "FREE-W", ProductKind.Wine, initialStock: 8, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "FREE-P", ProductKind.Pack, componentsJson: Components(wine.Id, 1));
        await harness.UpdateAsync(wine.Id, draft => draft.Price = "30.00");
        await harness.UpdateAsync(pack.Id, draft => draft.Price = "30.00");
        using var client = Fixture.Factory.CreateClient();

        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", new
        {
            lines = new[] { new { productId = wine.Id, quantity = 1 }, new { productId = pack.Id, quantity = 1 } },
            destination = new { postalCode = "37001", countryCode = "ES" }
        });

        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        var body = (await quote.Content.ReadFromJsonAsync<CheckoutQuoteDto>())!;
        Assert.Equal(6_000, body.ProductSubtotalCents);
        Assert.Equal(0, body.ShippingCents);
        Assert.Equal(0, body.ShippingTaxableBaseCents);
        Assert.Equal(0, body.ShippingVatCents);
        Assert.Equal(6_000, body.TotalCents);
    }

    [Fact]
    public async Task Refuses_to_price_shipping_when_the_vat_rates_differ()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "MIX-W", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var other = await harness.CreateProductAsync(categoryId, "MIX-S", ProductKind.Standard, initialStock: 4);
        await harness.UpdateAsync(other.Id, draft => draft.VatRate = "10.00");
        using var client = Fixture.Factory.CreateClient();

        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", new
        {
            lines = new[] { new { productId = wine.Id, quantity = 1 }, new { productId = other.Id, quantity = 1 } },
            destination = new { postalCode = "37001", countryCode = "ES" }
        });

        Assert.Equal(HttpStatusCode.Conflict, quote.StatusCode);
        Assert.Equal(ShippingTariff.MixedRatesMessage, await ErrorAsync(quote));
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task Does_not_keep_a_pending_order_when_the_reservation_fails()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var available = await harness.CreateProductAsync(categoryId, "FAIL-OK", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var missing = await harness.CreateProductAsync(categoryId, "FAIL-NO", ProductKind.Wine, vintage: "2023");
        using var client = Fixture.Factory.CreateClient();

        var response = await PlaceAsync(client, "fail-1", new
        {
            lines = new object[] { Line(available.Id, 1), Line(missing.Id, 1) },
            destination = Destination("37001"),
            customerName = "Ana Rivas",
            email = "ana@example.com",
            phone = "+34600111222",
            addressLine = "Calle Mayor 1",
            city = "Salamanca",
            province = "Salamanca"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(OrderStockRules.InsufficientStockMessage, await ErrorAsync(response));
        Assert.Equal(0, await CountOrdersAsync());
        Assert.Equal(4, (await harness.StockAsync(available.Id))!.Quantity);
        Assert.DoesNotContain(await harness.MovementsAsync(available.Id), item => item.Type == StockMovementType.Sale);
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_orders_takes_the_last_unit()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RACE-ORD", ProductKind.Wine, initialStock: 1, vintage: "2024");
        var barrier = new Barrier(2);
        var results = await Task.WhenAll(PlaceConcurrentAsync(wine.Id, barrier), PlaceConcurrentAsync(wine.Id, barrier));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result?.Message == OrderStockRules.InsufficientStockMessage);
        Assert.Equal(1, await CountOrdersAsync());
        Assert.Equal(0, (await harness.StockAsync(wine.Id))!.Quantity);
    }

    private async Task<Exception?> PlaceConcurrentAsync(Guid productId, Barrier barrier)
    {
        try
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICheckout>().PlaceGuestOrderAsync(
                Guid.NewGuid().ToString("N"),
                new PlaceGuestOrderRequest(
                    [new CheckoutLineRequest(productId, 1)],
                    new CheckoutDestinationRequest("37001", "ES"),
                    "Ana Rivas",
                    "ana@example.com",
                    "+34600111222",
                    "Calle Mayor 1",
                    "Salamanca",
                    "Salamanca",
                    null));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Fact]
    public async Task Repeating_the_idempotency_key_returns_the_same_order()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "IDEM-W", ProductKind.Wine, initialStock: 5, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();
        var body = Line(wine.Id, 2);

        var created = await PlaceAsync(client, "same-key", body);
        await harness.UpdateAsync(wine.Id, draft => draft.Active = false);
        var repeated = await PlaceAsync(client, "same-key", body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<GuestOrderDto>())!;
        var second = (await repeated.Content.ReadFromJsonAsync<GuestOrderDto>())!;
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(first.Number, second.Number);
        Assert.Equal(3, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(1, await CountOrdersAsync());
    }

    [Fact]
    public async Task Rejects_the_same_key_when_the_order_contents_change()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "IDEM-CHG", ProductKind.Wine, initialStock: 5, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await PlaceAsync(client, "same-key", Line(wine.Id, 1))).StatusCode);
        var changed = await PlaceAsync(client, "same-key", Line(wine.Id, 2));

        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(CheckoutLimits.IdempotencyConflictMessage, await ErrorAsync(changed));
        Assert.Equal(4, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(1, await CountOrdersAsync());
    }

    [Theory]
    [InlineData("07001")]
    [InlineData("35001")]
    [InlineData("38001")]
    [InlineData("51001")]
    [InlineData("52001")]
    public async Task Rejects_destinations_outside_peninsular_spain(string postalCode)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "GEO-" + postalCode, ProductKind.Wine, initialStock: 2, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();

        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(wine.Id, 1, postalCode));
        Assert.Equal(HttpStatusCode.BadRequest, quote.StatusCode);
        Assert.Equal(PeninsularSpain.OutsideMessage, await ErrorAsync(quote));
        Assert.Equal(2, (await harness.StockAsync(wine.Id))!.Quantity);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task Rejects_invalid_customer_data_and_does_not_expose_orders_by_id()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "VAL-W", ProductKind.Wine, initialStock: 2, vintage: "2024");
        using var client = Fixture.Factory.CreateClient();

        var missingKey = await client.PostAsJsonAsync("/api/v1/checkout/orders", Line(wine.Id, 1));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        var foreign = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(wine.Id, 1, "37001", "FR"));
        Assert.Equal(PeninsularSpain.OutsideMessage, await ErrorAsync(foreign));

        var invalidPostal = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(wine.Id, 1, "3700"));
        Assert.Equal(PeninsularSpain.InvalidPostalMessage, await ErrorAsync(invalidPostal));

        using var request = OrderRequest(new
        {
            lines = new[] { Line(wine.Id, 1) },
            destination = Destination("37001"),
            customerName = new string('A', 151),
            email = "ana@example.com",
            phone = "+34600111222",
            addressLine = "Calle Mayor 1",
            city = "Salamanca",
            province = "Salamanca"
        }, "long-name");
        var tooLong = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(0, await CountOrdersAsync());

        var hidden = await client.GetAsync($"/api/v1/checkout/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    private static async Task<CheckoutQuoteDto> QuoteAsync(HttpClient client, Guid productId, int quantity, string postalCode)
    {
        var response = await client.PostAsJsonAsync("/api/v1/checkout/quotes", QuoteBody(productId, quantity, postalCode));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CheckoutQuoteDto>())!;
    }

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient client, string key, object lineOrBody)
    {
        var body = lineOrBody.GetType().GetProperty("lines") is null
            ? new
            {
                lines = new[] { lineOrBody },
                destination = Destination("37001"),
                customerName = "Ana Rivas",
                email = "ana@example.com",
                phone = "+34600111222",
                addressLine = "Calle Mayor 1",
                city = "Salamanca",
                province = "Salamanca"
            }
            : lineOrBody;
        using var request = OrderRequest(body, key);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage OrderRequest(object body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return message;
    }

    private async Task<int> CountOrdersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders.CountAsync();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        return body!.Error;
    }

    private static object QuoteBody(Guid productId, int quantity, string postalCode = "37001", string countryCode = "ES")
    {
        return new
        {
            lines = new[] { Line(productId, quantity) },
            destination = Destination(postalCode, countryCode)
        };
    }

    private static object Line(Guid productId, int quantity, int? price = null)
    {
        return new { productId, quantity, price };
    }

    private static object Destination(string postalCode, string countryCode = "ES")
    {
        return new { postalCode, countryCode };
    }

    private static string Components(Guid productId, int quantity)
    {
        return System.Text.Json.JsonSerializer.Serialize(new[] { new { productId, quantity } });
    }

    private sealed record ErrorBody(string Error);
}
