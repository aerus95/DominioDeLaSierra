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

namespace DominioDeLaSierra.IntegrationTests;

public sealed class StripeWebhookTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Confirms_a_signed_payment_and_ignores_the_same_event()
    {
        var ready = await ReadyOrderAsync("wh-ok");
        var body = StripeWebhookSignatures.Event("evt_test_ok", "checkout.session.completed", ready.SessionId, ready.Total, orderId: ready.OrderId.ToString("D"));
        var (payload, header) = StripeWebhookSignatures.Sign(body);

        var first = await PostAsync(ready.Client, payload, header);
        var second = await PostAsync(ready.Client, payload, header);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await AssertPaidOnceAsync(ready.OrderId, ready.SessionId, stock: 4);
        Assert.Equal(1, await EventCountAsync(ready.OrderId));
    }

    [Fact]
    public async Task Rejects_a_bad_signature_and_a_changed_payload()
    {
        var ready = await ReadyOrderAsync("wh-sign");
        var body = StripeWebhookSignatures.Event("evt_test_bad", "checkout.session.completed", ready.SessionId, ready.Total, orderId: ready.OrderId.ToString("D"));
        var (_, header) = StripeWebhookSignatures.Sign(body);
        var changed = body.Replace(ready.Total.ToString(), (ready.Total + 1).ToString(), StringComparison.Ordinal);

        var missing = await PostAsync(ready.Client, body, null);
        var invalid = await PostAsync(ready.Client, body, "t=1,v1=deadbeef");
        var manipulated = await PostAsync(ready.Client, changed, header);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, manipulated.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(0, await EventCountAsync(ready.OrderId));
    }

    [Fact]
    public async Task Rejects_live_mode_and_does_not_mark_the_order_paid()
    {
        var ready = await ReadyOrderAsync("wh-live");
        var body = StripeWebhookSignatures.Event("evt_test_live", "checkout.session.completed", ready.SessionId, ready.Total, liveMode: true, orderId: ready.OrderId.ToString("D"));
        var (payload, header) = StripeWebhookSignatures.Sign(body);

        var response = await PostAsync(ready.Client, payload, header);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
    }

    [Fact]
    public async Task Records_a_second_event_for_the_same_payment_without_confirming_twice()
    {
        var ready = await ReadyOrderAsync("wh-two");
        await PostAsync(ready.Client, Paid(ready, "evt_test_one"));
        await PostAsync(ready.Client, Paid(ready, "evt_test_two", "checkout.session.async_payment_succeeded"));

        await AssertPaidOnceAsync(ready.OrderId, ready.SessionId, stock: 4);
        Assert.Equal(2, await EventCountAsync(ready.OrderId));
    }

    [Fact]
    public async Task Two_concurrent_webhooks_confirm_one_payment()
    {
        var ready = await ReadyOrderAsync("wh-race");
        var body = StripeWebhookSignatures.Event("evt_test_race", "checkout.session.completed", ready.SessionId, ready.Total, orderId: ready.OrderId.ToString("D"));
        var (payload, header) = StripeWebhookSignatures.Sign(body);
        await using var firstScope = Fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = Fixture.Factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IStripeWebhooks>();
        var second = secondScope.ServiceProvider.GetRequiredService<IStripeWebhooks>();

        var results = await Task.WhenAll(
            first.ReceiveAsync(payload, header),
            second.ReceiveAsync(payload, header));

        Assert.Contains(results, item => item.Disposition == StripeWebhookRules.Confirmed);
        await AssertPaidOnceAsync(ready.OrderId, ready.SessionId, stock: 4);
        Assert.Equal(1, await EventCountAsync(ready.OrderId));
    }

    [Fact]
    public async Task Does_not_confirm_a_wrong_amount_currency_unknown_session_or_missing_stock()
    {
        var amount = await ReadyOrderAsync("wh-amount");
        var currency = await ReadyOrderAsync("wh-currency");
        var unknown = await ReadyOrderAsync("wh-unknown");
        var released = await ReadyOrderAsync("wh-released");
        await ReleaseAsync(released.OrderId);

        var wrongAmount = await PostAsync(amount.Client, Paid(amount, "evt_test_amount", amountCents: amount.Total + 50));
        var wrongCurrency = await PostAsync(currency.Client, Paid(currency, "evt_test_currency", currency: "usd"));
        var unknownSession = await PostAsync(unknown.Client, Paid(unknown, "evt_test_missing", sessionId: "cs_test_desconocida"));
        var withoutStock = await PostAsync(released.Client, Paid(released, "evt_test_stock"));

        Assert.Equal(HttpStatusCode.OK, wrongAmount.StatusCode);
        Assert.Equal(HttpStatusCode.OK, wrongCurrency.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withoutStock.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(amount.OrderId));
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(currency.OrderId));
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(unknown.OrderId));
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(released.OrderId));
        Assert.Equal(StripeWebhookRules.AmountMessage, await AttentionAsync(amount.OrderId));
        Assert.Equal(StripeWebhookRules.CurrencyMessage, await AttentionAsync(currency.OrderId));
        Assert.Equal(StripeWebhookRules.StockMessage, await AttentionAsync(released.OrderId));
        Assert.Equal(0, await EventCountAsync(unknown.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(amount.OrderId));
        Assert.Equal(StockReservationStatus.Released, await ReservationAsync(released.OrderId));
    }

    [Fact]
    public async Task Confirms_a_payment_that_arrives_after_expiry_and_ignores_expiry_after_payment()
    {
        var late = await ReadyOrderAsync("wh-late");
        var early = await ReadyOrderAsync("wh-early");

        var expiredFirst = await PostAsync(late.Client, Paid(late, "evt_test_expired_first", "checkout.session.expired", paymentStatus: "unpaid"));
        Assert.Equal(HttpStatusCode.OK, expiredFirst.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(late.OrderId));
        Assert.Equal(StockReservationStatus.Reserved, await ReservationAsync(late.OrderId));

        await PostAsync(late.Client, Paid(late, "evt_test_paid_later"));
        await AssertPaidOnceAsync(late.OrderId, late.SessionId, stock: 4);

        await PostAsync(early.Client, Paid(early, "evt_test_paid_first"));
        await PostAsync(early.Client, Paid(early, "evt_test_expired_later", "checkout.session.expired", paymentStatus: "unpaid"));
        await AssertPaidOnceAsync(early.OrderId, early.SessionId, stock: 4);
        Assert.Equal(StockReservationStatus.Confirmed, await ReservationAsync(early.OrderId));
    }

    [Fact]
    public async Task Retries_after_a_database_failure_without_having_stored_the_event()
    {
        var ready = await ReadyOrderAsync("wh-retry");
        var probe = Fixture.Factory.Services.GetRequiredService<StripeWebhookFailureProbe>();
        probe.FailNext();
        var (payload, header) = StripeWebhookSignatures.Sign(StripeWebhookSignatures.Event(
            "evt_test_retry",
            "checkout.session.completed",
            ready.SessionId,
            ready.Total,
            orderId: ready.OrderId.ToString("D")));

        var failed = await PostAsync(ready.Client, payload, header);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
        Assert.Equal(0, await EventCountAsync(ready.OrderId));

        var retried = await PostAsync(ready.Client, payload, header);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        await AssertPaidOnceAsync(ready.OrderId, ready.SessionId, stock: 4);
    }

    [Fact]
    public async Task The_success_route_cannot_mark_an_order_paid()
    {
        var ready = await ReadyOrderAsync("wh-browser");
        var confirm = await ready.Client.PostAsync($"/api/v1/checkout/orders/{ready.OrderId}/paid", null);
        var success = await ready.Client.PostAsync("/checkout/exito", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.False(confirm.IsSuccessStatusCode);
        Assert.False(success.IsSuccessStatusCode);
        Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(ready.OrderId));
    }

    private static (string Payload, string Header) Paid(
        ReadyOrder order,
        string eventId,
        string type = "checkout.session.completed",
        string paymentStatus = "paid",
        long? amountCents = null,
        string currency = "eur",
        string? sessionId = null)
    {
        return StripeWebhookSignatures.Sign(StripeWebhookSignatures.Event(
            eventId,
            type,
            sessionId ?? order.SessionId,
            amountCents ?? order.Total,
            paymentStatus,
            currency,
            orderId: order.OrderId.ToString("D")));
    }

    private async Task<ReadyOrder> ReadyOrderAsync(string key)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, key, ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(new PlaceGuestOrderRequest(
                [new CheckoutLineRequest(wine.Id, 1)],
                new CheckoutDestinationRequest("37001", "ES"),
                "Ana Rivas",
                "ana@example.com",
                "+34600111222",
                "Calle Mayor 1",
                "Salamanca",
                "Salamanca",
                null))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key + "-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        var payment = JsonSerializer.Deserialize<CheckoutPaymentDto>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sessionId = await db.Payments.Where(item => item.OrderId == payment.OrderId).Select(item => item.StripeCheckoutSessionId).SingleAsync();
        return new ReadyOrder(client, payment.OrderId, wine.Id, sessionId!, payment.TotalCents);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string? header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", header);
        }

        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, (string Payload, string Header) signed)
    {
        return PostAsync(client, signed.Payload, signed.Header);
    }

    private async Task AssertPaidOnceAsync(Guid orderId, string sessionId, int stock)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        var payment = order.Payments.Single(item => item.StripeCheckoutSessionId == sessionId);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.StartsWith("pi_test_", payment.StripePaymentIntentId, StringComparison.Ordinal);
        Assert.Equal(StockReservationStatus.Confirmed, await ReservationAsync(orderId));
        var productId = await db.OrderItems.Where(line => line.OrderId == orderId).Select(line => line.ProductId).SingleAsync();
        Assert.Equal(stock, await db.Stocks.Where(item => item.ProductId == productId).Select(item => item.Quantity).SingleAsync());
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Orders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync();
    }

    private async Task<int> EventCountAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var paymentIds = db.Payments.Where(item => item.OrderId == orderId).Select(item => item.Id);
        return await db.PaymentEvents.CountAsync(item => paymentIds.Contains(item.PaymentId));
    }

    private async Task<string?> AttentionAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var paymentIds = db.Payments.Where(item => item.OrderId == orderId).Select(item => item.Id);
        return await db.PaymentEvents.Where(item => paymentIds.Contains(item.PaymentId)).Select(item => item.AttentionReason).SingleAsync();
    }

    private async Task<StockReservationStatus> ReservationAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.StockReservations.Where(item => item.OrderId == orderId).Select(item => item.Status).SingleAsync();
    }

    private async Task ReleaseAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrderStock>().ReleaseAsync(orderId, DateTimeOffset.UtcNow);
    }

    private sealed record ReadyOrder(HttpClient Client, Guid OrderId, Guid ProductId, string SessionId, long Total);
}
