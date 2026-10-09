using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stripe.Checkout;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class StripeCheckoutSessionTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Creates_a_test_session_for_the_persisted_total_and_replays_it()
    {
        var (client, order, token, stub) = await PlaceOrderAsync("stripe-ok");
        await ExtendHoldAsync(order.OrderId);
        stub.Reset();

        var first = await StartAsync(client, order.OrderId, token, new { amountCents = 1, price = 5 });
        var firstJson = await first.Content.ReadAsStringAsync();
        var second = await StartAsync(client, order.OrderId, token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.DoesNotContain("ana@example.com", firstJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, firstJson, StringComparison.Ordinal);
        var session = JsonSerializer.Deserialize<CheckoutSessionDto>(firstJson, JsonOptions())!;
        Assert.StartsWith("https://checkout.stripe.com/", session.CheckoutUrl, StringComparison.Ordinal);
        Assert.Equal(session.CheckoutUrl, (await second.Content.ReadFromJsonAsync<CheckoutSessionDto>(JsonOptions()))!.CheckoutUrl);
        Assert.Equal(order.TotalCents, Assert.Single(stub.Calls).AmountCents);
        Assert.True(session.ExpiresAt >= DateTimeOffset.UtcNow.Add(StripeCheckoutExpiry.Minimum).AddSeconds(-5));

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == order.OrderId);
        var payment = Assert.Single(stored.Payments);
        Assert.Equal(OrderStatus.PendingPayment, stored.Status);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(stored.TotalCents, payment.AmountCents);
        Assert.True(payment.CheckoutExpiresAt <= stored.ReservationExpiresAt.Subtract(StripeCheckoutExpiry.ReservationTail));
        Assert.StartsWith("cs_test_", payment.StripeCheckoutSessionId, StringComparison.Ordinal);
        Assert.Equal(CheckoutAccessToken.Hash(token), stored.CheckoutAccessTokenHash);
        Assert.DoesNotContain(token, stored.CheckoutAccessTokenHash, StringComparison.Ordinal);
        Assert.Equal(StockReservationStatus.Reserved, await db.StockReservations.SingleAsync(item => item.OrderId == stored.Id) is { } reservation
            ? reservation.Status
            : throw new InvalidOperationException("sin reserva"));
    }

    [Fact]
    public async Task Does_not_open_checkout_when_the_reservation_cannot_cover_stripe()
    {
        var (client, order, token, stub) = await PlaceOrderAsync("stripe-short");
        await AgeReservationAsync(order.OrderId, TimeSpan.FromMinutes(10));
        stub.Reset();

        var response = await StartAsync(client, order.OrderId, token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(StripeCheckoutExpiry.OutsideStripeWindowMessage, await ErrorAsync(response));
        Assert.Empty(stub.Calls);
        await AssertStillPendingWithoutPaymentAsync(order.OrderId);
    }

    [Fact]
    public async Task Does_not_open_checkout_for_an_expired_paid_cancelled_or_unreserved_order()
    {
        var expired = await PlaceOrderAsync("stripe-expired");
        await AgeReservationAsync(expired.Order.OrderId, TimeSpan.FromMinutes(40));
        var missing = await PlaceOrderAsync("stripe-missing");
        await ReleaseAsync(missing.Order.OrderId);
        var paid = await PlaceOrderAsync("stripe-paid");
        await MarkPaidAsync(paid.Order.OrderId);
        var cancelled = await PlaceOrderAsync("stripe-cancelled");
        await CancelAsync(cancelled.Order.OrderId);
        expired.Stub.Reset();

        Assert.Equal(StripeCheckoutExpiry.ExpiredMessage, await ErrorAsync(await StartAsync(expired.Client, expired.Order.OrderId, expired.Token)));
        Assert.Equal(OrderStockRules.MissingReservationMessage, await ErrorAsync(await StartAsync(missing.Client, missing.Order.OrderId, missing.Token)));
        Assert.Equal(CheckoutLimits.PaidOrderMessage, await ErrorAsync(await StartAsync(paid.Client, paid.Order.OrderId, paid.Token)));
        Assert.Equal(CheckoutLimits.CancelledOrderMessage, await ErrorAsync(await StartAsync(cancelled.Client, cancelled.Order.OrderId, cancelled.Token)));
        Assert.Empty(expired.Stub.Calls);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(expired.Order.OrderId));
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(missing.Order.OrderId));
    }

    [Fact]
    public async Task Keeps_the_order_and_reservation_when_stripe_fails_and_allows_another_attempt()
    {
        var (client, order, token, stub) = await PlaceOrderAsync("stripe-fail");
        await ExtendHoldAsync(order.OrderId);
        stub.Reset();
        stub.Failure = new StripeCheckoutException("detalle interno");

        var failed = await StartAsync(client, order.OrderId, token);
        stub.Failure = null;
        var retried = await StartAsync(client, order.OrderId, token);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(CheckoutLimits.PaymentUnavailableMessage, await ErrorAsync(failed));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == order.OrderId);
        Assert.Equal(OrderStatus.PendingPayment, stored.Status);
        Assert.Equal(PaymentStatus.Failed, stored.Payments.Single(item => item.StripeCheckoutSessionId == null).Status);
        Assert.Equal(PaymentStatus.Pending, stored.Payments.Single(item => item.StripeCheckoutSessionId != null).Status);
        Assert.Equal(StockReservationStatus.Reserved, (await db.StockReservations.SingleAsync(item => item.OrderId == stored.Id)).Status);
    }

    [Fact]
    public async Task Two_concurrent_requests_share_one_checkout_session()
    {
        var (_, order, token, stub) = await PlaceOrderAsync("stripe-race");
        await ExtendHoldAsync(order.OrderId);
        stub.Reset();
        stub.Delay = TimeSpan.FromMilliseconds(250);
        await using var firstScope = Fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = Fixture.Factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IOrderPaymentSessions>();
        var second = secondScope.ServiceProvider.GetRequiredService<IOrderPaymentSessions>();

        var results = await Task.WhenAll(
            first.StartAsync(order.OrderId, token),
            second.StartAsync(order.OrderId, token));

        Assert.Equal(results[0].CheckoutUrl, results[1].CheckoutUrl);
        Assert.Equal(2, stub.Calls.Count);
        Assert.Equal(stub.Calls[0].Key, stub.Calls[1].Key);
        Assert.All(stub.Calls, call => Assert.Equal(order.TotalCents, call.AmountCents));
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = Assert.Single(await db.Payments.Where(item => item.OrderId == order.OrderId).ToListAsync());
        Assert.StartsWith("cs_test_", payment.StripeCheckoutSessionId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_authorize_checkout_with_the_order_id_alone()
    {
        var (client, order, token, stub) = await PlaceOrderAsync("stripe-auth");
        stub.Reset();

        var missing = await StartAsync(client, order.OrderId, null);
        var wrong = await StartAsync(client, order.OrderId, "otro-token-no-valido");
        var unknown = await StartAsync(client, Guid.NewGuid(), token);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(await ErrorAsync(wrong), await ErrorAsync(unknown));
        Assert.Empty(stub.Calls);
    }

    [Fact]
    public void Builds_a_checkout_session_without_automatic_tax_or_customer_data()
    {
        var orderId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var options = StripeCheckoutRequest.Create(new StripeCheckoutDraft(
            orderId,
            "DS-20261009-TEST",
            2_690,
            [
                new StripeCheckoutLines.Line("Dominium", 1, 1_890),
                new StripeCheckoutLines.Line(StripeCheckoutLines.ShippingName, 1, 800)
            ],
            expires,
            "http://localhost:4200/checkout/exito",
            "http://localhost:4200/checkout/cancelado"));

        Assert.Equal("payment", options.Mode);
        Assert.False(options.AutomaticTax.Enabled);
        Assert.Equal(expires.UtcDateTime, options.ExpiresAt);
        Assert.Equal("eur", options.LineItems[0].PriceData.Currency);
        Assert.Equal(1_890, options.LineItems[0].PriceData.UnitAmount);
        Assert.Equal(800, options.LineItems[1].PriceData.UnitAmount);
        Assert.Null(options.LineItems[0].PriceData.TaxBehavior);
        Assert.Null(options.CustomerEmail);
        Assert.Equal(orderId.ToString("D"), options.Metadata["order_id"]);
        Assert.Equal(2, options.Metadata.Count);
        Assert.DoesNotContain("email", JsonSerializer.Serialize(options.Metadata), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quote_does_not_reserve_so_a_delayed_form_still_gets_thirty_minutes()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "stripe-delay", ProductKind.Wine, initialStock: 4, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        var quote = await client.PostAsJsonAsync("/api/v1/checkout/quotes", new
        {
            lines = new[] { new { productId = wine.Id, quantity = 1 } },
            destination = new { postalCode = "37001", countryCode = "ES" }
        });

        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.Orders.CountAsync());
            Assert.Equal(0, await db.StockReservations.CountAsync());
        }

        var paid = await PayAsync(client, wine.Id, "stripe-delay-pay");
        var body = await paid.Content.ReadAsStringAsync();
        var payment = JsonSerializer.Deserialize<CheckoutPaymentDto>(body, JsonOptions())!;

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.DoesNotContain("ana@example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(OrderStatus.PendingPayment), payment.Status);
        Assert.DoesNotContain("Paid", payment.Status, StringComparison.Ordinal);
        Assert.True(payment.ExpiresAt >= DateTimeOffset.UtcNow.Add(StripeCheckoutExpiry.Minimum).AddSeconds(-5));
        Assert.True(payment.ReservationExpiresAt - payment.ExpiresAt >= StripeCheckoutExpiry.ReservationTail);
        Assert.StartsWith("https://checkout.stripe.com/", payment.CheckoutUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reuses_the_order_and_reservation_when_the_payment_is_retried()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "stripe-retry-key", ProductKind.Wine, initialStock: 3, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        var stub = Fixture.Factory.Services.GetRequiredService<StripeCheckoutStub>();
        stub.Reset();
        var key = "stripe-same-" + Guid.NewGuid().ToString("N");

        var first = await PayAsync(client, wine.Id, key);
        var created = JsonSerializer.Deserialize<CheckoutPaymentDto>(await first.Content.ReadAsStringAsync(), JsonOptions())!;
        var second = await PayAsync(client, wine.Id, key);
        var replayedJson = await second.Content.ReadAsStringAsync();
        var replayed = JsonSerializer.Deserialize<CheckoutPaymentDto>(replayedJson, JsonOptions())!;

        Assert.Equal(created.CheckoutUrl, replayed.CheckoutUrl);
        Assert.Equal(created.OrderId, replayed.OrderId);
        Assert.False(string.IsNullOrWhiteSpace(created.CheckoutAccessToken));
        Assert.DoesNotContain(created.CheckoutAccessToken!, replayedJson, StringComparison.Ordinal);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync(item => item.Id == created.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, (await db.StockReservations.SingleAsync(item => item.OrderId == created.OrderId)).Status);
        Assert.Equal(nameof(OrderStatus.PendingPayment), replayed.Status);
    }

    [Fact]
    public async Task Keeps_the_hold_when_stripe_fails_and_releases_it_when_checkout_no_longer_fits()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "stripe-release", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        var stub = Fixture.Factory.Services.GetRequiredService<StripeCheckoutStub>();
        stub.Reset();
        stub.Failure = new StripeCheckoutException("detalle interno");
        var key = "stripe-fail-pay-" + Guid.NewGuid().ToString("N");

        var failed = await PayAsync(client, wine.Id, key);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(CheckoutLimits.PaymentUnavailableMessage, await ErrorAsync(failed));
        Guid orderId;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.Include(item => item.Payments).SingleAsync();
            orderId = stored.Id;
            Assert.Equal(OrderStatus.PendingPayment, stored.Status);
            Assert.Equal(PaymentStatus.Failed, Assert.Single(stored.Payments).Status);
            Assert.Equal(StockReservationStatus.Reserved, (await db.StockReservations.SingleAsync(item => item.OrderId == stored.Id)).Status);
            Assert.Equal(4, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
        }

        stub.Failure = null;
        var retried = await PayAsync(client, wine.Id, key);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        await SqueezeHoldAsync(orderId);
        var released = await PayAsync(client, wine.Id, key);

        Assert.Equal(HttpStatusCode.Conflict, released.StatusCode);
        Assert.Equal(CheckoutLimits.HoldReleasedMessage, await ErrorAsync(released));
        await using var check = Fixture.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(OrderStatus.Cancelled, await database.Orders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync());
        Assert.Equal(StockReservationStatus.Released, (await database.StockReservations.SingleAsync(item => item.OrderId == orderId)).Status);
        Assert.Equal(5, await database.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
        Assert.NotEqual(OrderStatus.Paid, await database.Orders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Two_concurrent_payments_share_one_order_and_one_reservation()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "stripe-pay-race", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var stub = Fixture.Factory.Services.GetRequiredService<StripeCheckoutStub>();
        stub.Reset();
        stub.Delay = TimeSpan.FromMilliseconds(250);
        var key = "stripe-pay-race-" + Guid.NewGuid().ToString("N");
        var request = GuestRequest(wine.Id);
        await using var firstScope = Fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = Fixture.Factory.Services.CreateAsyncScope();

        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<IOrderPaymentSessions>().PayAsync(key, request),
            secondScope.ServiceProvider.GetRequiredService<IOrderPaymentSessions>().PayAsync(key, request));

        Assert.Equal(results[0].CheckoutUrl, results[1].CheckoutUrl);
        Assert.Equal(results[0].OrderId, results[1].OrderId);
        Assert.Equal(results[0].TotalCents, results[1].TotalCents);
        Assert.All(results, item => Assert.Equal(nameof(OrderStatus.PendingPayment), item.Status));
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(1, await db.StockReservations.CountAsync(item => item.Status == StockReservationStatus.Reserved));
        Assert.Equal(4, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Does_not_reserve_when_stock_runs_out_before_payment()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "stripe-stock", ProductKind.Wine, initialStock: 1, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        var quoted = await client.PostAsJsonAsync("/api/v1/checkout/quotes", new
        {
            lines = new[] { new { productId = wine.Id, quantity = 1 } },
            destination = new { postalCode = "37001", countryCode = "ES" }
        });
        Assert.Equal(HttpStatusCode.OK, quoted.StatusCode);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Stocks" SET "Quantity" = 0 WHERE "ProductId" = {wine.Id}""");
        }

        var paid = await PayAsync(client, wine.Id, "stripe-stock-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);
        Assert.Equal(OrderStockRules.InsufficientStockMessage, await ErrorAsync(paid));
        await using var check = Fixture.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await database.Orders.CountAsync());
        Assert.Equal(0, await database.StockReservations.CountAsync());
    }

    private async Task<(HttpClient Client, GuestOrderDto Order, string Token, StripeCheckoutStub Stub)> PlaceOrderAsync(string key)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, key, ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = JsonContent.Create(new
            {
                lines = new[] { new { productId = wine.Id, quantity = 1, price = 1 } },
                destination = new { postalCode = "37001", countryCode = "ES" },
                customerName = "Ana Rivas",
                email = "ana@example.com",
                phone = "+34600111222",
                addressLine = "Calle Mayor 1",
                city = "Salamanca",
                province = "Salamanca"
            })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key + "-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = JsonSerializer.Deserialize<GuestOrderDto>(json, JsonOptions())!;
        Assert.False(string.IsNullOrWhiteSpace(order.CheckoutAccessToken));
        return (client, order, order.CheckoutAccessToken!, Fixture.Factory.Services.GetRequiredService<StripeCheckoutStub>());
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, Guid orderId, string? token, object? extra = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payment-sessions")
        {
            Content = extra is null
                ? JsonContent.Create(new { orderId })
                : JsonContent.Create(new { orderId, amountCents = 1, price = 5 })
        };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Checkout-Access-Token", token);
        }

        return await client.SendAsync(request);
    }

    private static PlaceGuestOrderRequest GuestRequest(Guid productId)
    {
        return new PlaceGuestOrderRequest(
            [new CheckoutLineRequest(productId, 1)],
            new CheckoutDestinationRequest("37001", "ES"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "Salamanca",
            "Salamanca",
            null);
    }

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid productId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(GuestRequest(productId))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task SqueezeHoldAsync(Guid orderId)
    {
        var reservation = DateTimeOffset.UtcNow.AddMinutes(31);
        var session = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Orders" SET "ReservationExpiresAt" = {reservation} WHERE "Id" = {orderId}""");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Payments" SET "CheckoutExpiresAt" = {session} WHERE "OrderId" = {orderId} AND "CheckoutUrl" IS NOT NULL""");
    }

    private async Task ExtendHoldAsync(Guid orderId)
    {
        var expires = DateTimeOffset.UtcNow.Add(StripeCheckoutExpiry.PaymentHold);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Orders" SET "ReservationExpiresAt" = {expires} WHERE "Id" = {orderId}""");
    }

    private async Task AgeReservationAsync(Guid orderId, TimeSpan age)
    {
        var created = DateTimeOffset.UtcNow.Subtract(age);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Orders" SET "CreatedAt" = {created}, "ReservationExpiresAt" = {created.AddMinutes(30)} WHERE "Id" = {orderId}""");
    }

    private async Task ReleaseAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrderStock>().ReleaseAsync(orderId, DateTimeOffset.UtcNow);
    }

    private async Task MarkPaidAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        var payment = order.AddPayment(Guid.NewGuid(), DateTimeOffset.UtcNow);
        order.MarkPaid(payment.Id, DateTimeOffset.UtcNow.AddSeconds(1));
        await db.SaveChangesAsync();
    }

    private async Task CancelAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.SingleAsync(item => item.Id == orderId);
        order.Cancel(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task AssertStillPendingWithoutPaymentAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Empty(order.Payments);
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Orders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        return body!.Error;
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private sealed record ErrorBody(string Error);
}
