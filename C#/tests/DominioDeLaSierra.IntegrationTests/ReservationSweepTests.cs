using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class ReservationSweepTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Releases_an_expired_hold_without_a_session_after_the_grace_and_waits_inside_it()
    {
        var ready = await PlaceWithoutSessionAsync("sweep-none");
        var recent = await PlaceWithoutSessionAsync("sweep-grace");
        await AgeAsync(ready.OrderId, TimeSpan.FromMinutes(5));
        await AgeAsync(recent.OrderId, TimeSpan.FromSeconds(30));

        var result = await SweepAsync();

        Assert.Equal(1, result.Released);
        Assert.Equal(0, result.Confirmed);
        await AssertReleasedAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(recent.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(recent.OrderId));
        Assert.Equal(4, await StockAsync(recent.ProductId));
    }

    [Fact]
    public async Task Releases_an_expired_hold_when_stripe_says_the_session_expired()
    {
        var ready = await PayAsync("sweep-expired");
        Stub().SetInspection(ready.OrderId, Expired(ready.SessionId));

        var result = await SweepAsync();

        Assert.Equal(1, result.Released);
        await AssertReleasedAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(1, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task Keeps_stock_when_the_expired_hold_still_has_an_open_session()
    {
        var ready = await PayAsync("sweep-open");
        Stub().SetInspection(ready.OrderId, new StripeSessionInspection(
            StripeCheckoutFact.Open, ready.SessionId, null, null, null, null, null, null));

        var result = await SweepAsync();

        Assert.Equal(0, result.Released);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(ready.OrderId));
        Assert.Equal(4, await StockAsync(ready.ProductId));
    }

    [Fact]
    public async Task Confirms_a_paid_session_before_its_webhook_arrives()
    {
        var ready = await PayAsync("sweep-paid");
        Stub().SetInspection(ready.OrderId, Paid(ready));

        var first = await SweepAsync();
        var second = await SweepAsync();
        var (payload, header) = StripeWebhookSignatures.Sign(StripeWebhookSignatures.Event(
            "evt_test_after_sweep",
            "checkout.session.completed",
            ready.SessionId,
            ready.Total,
            orderId: ready.OrderId.ToString("D")));
        var webhook = await PostAsync(ready.Client, payload, header);

        Assert.Equal(1, first.Confirmed);
        Assert.Equal(0, second.Processed);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        await AssertPaidAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(0, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task Keeps_stock_while_an_asynchronous_payment_is_pending()
    {
        var ready = await PayAsync("sweep-async");
        Stub().SetInspection(ready.OrderId, new StripeSessionInspection(
            StripeCheckoutFact.Pending, ready.SessionId, "pi_test_pending", null, null, null, null, null));

        var result = await SweepAsync();

        Assert.Equal(0, result.Released);
        Assert.Equal(0, result.Failed);
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(ready.OrderId));
        Assert.Equal(4, await StockAsync(ready.ProductId));
    }

    [Fact]
    public async Task Keeps_stock_when_stripe_is_unavailable()
    {
        var ready = await PayAsync("sweep-down");
        Stub().SetInspection(ready.OrderId, StripeSessionInspection.Unavailable());

        var result = await SweepAsync();

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Released);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(ready.OrderId));
        Assert.Equal(4, await StockAsync(ready.ProductId));
    }

    [Fact]
    public async Task Does_not_release_when_a_webhook_confirms_during_the_stripe_call()
    {
        var ready = await PayAsync("sweep-race");
        var stub = Stub();
        stub.SetInspection(ready.OrderId, Expired(ready.SessionId));
        stub.InspectHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweeping = SweepAsync();
        Assert.True(SpinWait.SpinUntil(() => stub.InspectStarted > 0, TimeSpan.FromSeconds(5)));
        var (payload, header) = StripeWebhookSignatures.Sign(StripeWebhookSignatures.Event(
            "evt_test_during_sweep",
            "checkout.session.completed",
            ready.SessionId,
            ready.Total,
            orderId: ready.OrderId.ToString("D")));
        var webhook = await PostAsync(ready.Client, payload, header);
        stub.InspectHold.SetResult(true);
        var result = await sweeping;

        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal(0, result.Released);
        await AssertPaidAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(0, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task Two_workers_release_one_reservation()
    {
        var ready = await PayAsync("sweep-workers");
        Stub().SetInspection(ready.OrderId, Expired(ready.SessionId));
        await using var firstScope = Fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = Fixture.Factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IReservationSweep>();
        var second = secondScope.ServiceProvider.GetRequiredService<IReservationSweep>();

        var results = await Task.WhenAll(first.RunOnceAsync(), second.RunOnceAsync());

        Assert.Equal(1, results.Sum(item => item.Released));
        await AssertReleasedAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(1, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task A_second_sweep_does_not_release_again()
    {
        var ready = await PayAsync("sweep-twice");
        Stub().SetInspection(ready.OrderId, Expired(ready.SessionId));

        await SweepAsync();
        var again = await SweepAsync();

        Assert.Equal(0, again.Processed);
        await AssertReleasedAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(1, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task Recovers_a_paid_session_whose_response_was_lost_and_does_not_release_it()
    {
        var ready = await PayAsync("sweep-lost");
        await ForgetSessionAsync(ready.OrderId);
        Stub().SetInspection(ready.OrderId, Paid(ready, "cs_test_recovered"));

        var result = await SweepAsync();

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(0, result.Released);
        await AssertPaidAsync(ready.OrderId, ready.ProductId);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sessionId = await db.Payments.Where(item => item.OrderId == ready.OrderId).Select(item => item.StripeCheckoutSessionId).SingleAsync();
        Assert.Equal("cs_test_recovered", sessionId);
    }

    [Fact]
    public async Task Retries_a_release_after_a_database_failure()
    {
        var ready = await PayAsync("sweep-retry");
        Stub().SetInspection(ready.OrderId, Expired(ready.SessionId));
        var probe = Fixture.Factory.Services.GetRequiredService<ReservationSweepFailureProbe>();
        probe.FailNext();

        var failed = await SweepAsync();

        Assert.Equal(1, failed.Failed);
        Assert.Equal(0, failed.Released);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(4, await StockAsync(ready.ProductId));
        var retried = await SweepAsync();
        Assert.Equal(1, retried.Released);
        await AssertReleasedAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(1, await CancellationsAsync(ready.ProductId));
    }

    [Fact]
    public async Task Records_a_paid_session_that_no_longer_has_reserved_stock_without_fulfilling_the_order()
    {
        var ready = await PayAsync("sweep-nostock");
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOrderStock>().ReleaseAsync(ready.OrderId, DateTimeOffset.UtcNow);
        }

        Stub().SetInspection(ready.OrderId, Paid(ready));
        var result = await SweepAsync();

        Assert.Equal(1, result.Incidents);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(StockReservationStatus.Released, await ReservationAsync(ready.OrderId));
        Assert.Equal(5, await StockAsync(ready.ProductId));
        Assert.Equal(StripeWebhookRules.StockMessage, await AttentionAsync(ready.OrderId));
    }

    [Fact]
    public async Task Does_not_release_a_paid_order_or_a_confirmed_reservation()
    {
        var ready = await PayAsync("sweep-safe");
        Stub().SetInspection(ready.OrderId, Paid(ready));
        await SweepAsync();
        Stub().SetInspection(ready.OrderId, Expired(ready.SessionId));

        var result = await SweepAsync();

        Assert.Equal(0, result.Processed);
        await AssertPaidAsync(ready.OrderId, ready.ProductId);
        Assert.Equal(0, await CancellationsAsync(ready.ProductId));
        Assert.False(Fixture.Factory.Services.GetRequiredService<IOptions<ReservationSweepOptions>>().Value.SweepEnabled);
    }

    private StripeCheckoutStub Stub()
    {
        var stub = Fixture.Factory.Services.GetRequiredService<StripeCheckoutStub>();
        stub.Reset();
        Fixture.Factory.Services.GetRequiredService<ReservationSweepFailureProbe>().FailNext(0);
        return stub;
    }

    private async Task<ReservationSweepResult> SweepAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReservationSweep>().RunOnceAsync();
    }

    private async Task<ReadyOrder> PayAsync(string key)
    {
        Stub();
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, key, ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(Body(wine.Id))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key + "-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        var payment = JsonSerializer.Deserialize<CheckoutPaymentDto>(await response.Content.ReadAsStringAsync(), JsonOptions())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AgeAsync(payment.OrderId, TimeSpan.FromMinutes(5));
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sessionId = await db.Payments.Where(item => item.OrderId == payment.OrderId).Select(item => item.StripeCheckoutSessionId).SingleAsync();
        return new ReadyOrder(client, payment.OrderId, wine.Id, sessionId!, payment.TotalCents);
    }

    private async Task<ReadyOrder> PlaceWithoutSessionAsync(string key)
    {
        Stub();
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, key, ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = JsonContent.Create(Body(wine.Id))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key + "-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        var order = JsonSerializer.Deserialize<GuestOrderDto>(await response.Content.ReadAsStringAsync(), JsonOptions())!;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new ReadyOrder(client, order.OrderId, wine.Id, "", order.TotalCents);
    }

    private static PlaceGuestOrderRequest Body(Guid productId)
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

    private async Task AgeAsync(Guid orderId, TimeSpan past)
    {
        var expires = DateTimeOffset.UtcNow.Subtract(past);
        var created = expires.AddMinutes(-40);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Orders" SET "CreatedAt" = {created}, "ReservationExpiresAt" = {expires} WHERE "Id" = {orderId}""");
    }

    private async Task ForgetSessionAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Payments" SET "StripeCheckoutSessionId" = NULL, "CheckoutUrl" = NULL, "CheckoutExpiresAt" = NULL WHERE "OrderId" = {orderId}""");
    }

    private static StripeSessionInspection Paid(ReadyOrder order, string? sessionId = null)
    {
        return new StripeSessionInspection(
            StripeCheckoutFact.Paid,
            sessionId ?? order.SessionId,
            "pi_test_" + order.OrderId.ToString("N"),
            order.Total,
            "eur",
            order.OrderId.ToString("D"),
            order.OrderId.ToString("D"),
            null);
    }

    private static StripeSessionInspection Expired(string sessionId)
    {
        return new StripeSessionInspection(StripeCheckoutFact.ExpiredUnpaid, sessionId, null, null, null, null, null, null);
    }

    private async Task AssertReleasedAsync(Guid orderId, Guid productId)
    {
        Assert.Equal(OrderStatus.Expired, await StatusAsync(orderId));
        Assert.Equal(StockReservationStatus.Released, await ReservationAsync(orderId));
        Assert.Equal(5, await StockAsync(productId));
    }

    private async Task AssertPaidAsync(Guid orderId, Guid productId)
    {
        Assert.Equal(OrderStatus.Paid, await StatusAsync(orderId));
        Assert.Equal(StockReservationStatus.Confirmed, await ReservationAsync(orderId));
        Assert.Equal(4, await StockAsync(productId));
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Orders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync();
    }

    private async Task<StockReservationStatus> ReservationAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.StockReservations.Where(item => item.OrderId == orderId).Select(item => item.Status).SingleAsync();
    }

    private async Task<int> StockAsync(Guid productId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Stocks.Where(item => item.ProductId == productId).Select(item => item.Quantity).SingleAsync();
    }

    private async Task<int> CancellationsAsync(Guid productId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stockId = await db.Stocks.Where(item => item.ProductId == productId).Select(item => item.Id).SingleAsync();
        return await db.StockMovements.CountAsync(item => item.StockId == stockId && item.Type == StockMovementType.Cancellation);
    }

    private async Task<string?> AttentionAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var paymentIds = db.Payments.Where(item => item.OrderId == orderId).Select(item => item.Id);
        return await db.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId)).Select(item => item.AttentionReason).SingleAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", header);
        return await client.SendAsync(request);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private sealed record ReadyOrder(HttpClient Client, Guid OrderId, Guid ProductId, string SessionId, long Total);
}
