using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

/// <summary>
/// Estas pruebas usan PayPalOrderScript. No llaman a api-m.sandbox.paypal.com
/// y no equivalen a un cobro real en PayPal Sandbox.
/// </summary>
public sealed class PayPalCheckoutTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const string Secret = "clave-paypal-local";
    private const string WebhookId = "8AB12345CD678901E";
    private const string CertUrl = "https://api.sandbox.paypal.com/v1/notifications/certs/CERT-LOCAL";

    [Fact]
    public async Task Creates_one_sandbox_order_for_the_persisted_total_and_reuses_it()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-CREATE", 5);
        var client = host.CreateClient();
        var repeated = await PayAsync(client, started.ProductId, "PayPal", started.Key);
        var outcome = await OutcomeAsync(client, started.Payment);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        var body = started.Body;

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.Equal("PayPal", started.Payment.Provider);
        Assert.Equal("GET", started.Payment.RedirectMethod);
        Assert.StartsWith("https://www.sandbox.paypal.com/", started.Payment.CheckoutUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("www.paypal.com", started.Payment.CheckoutUrl, StringComparison.Ordinal);
        Assert.Equal(PayPalRules.Currency, started.Payment.Currency);
        Assert.Equal(1, script.CreateCalls);
        Assert.Equal(script.CreateRequestIds[0], Assert.Single(script.CreateRequestIds.Distinct()));
        Assert.True(script.CreateRequestIds[0].Length <= PayPalRules.RequestIdMaxLength);
        var snapshot = await SnapshotAsync(host, started);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(OrderAmounts.ReservationDuration, snapshot.Hold);
        Assert.NotEqual(StripeCheckoutExpiry.PaymentHold, snapshot.Hold);
        Assert.Equal(started.Payment.TotalCents, snapshot.AmountCents);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(HttpStatusCode.OK, outcome.StatusCode);
        Assert.Equal(nameof(OrderStatus.PendingPayment), (await outcome.Content.ReadFromJsonAsync<CheckoutOutcomeDto>(JsonOptions()))!.Status);
        Assert.Equal(0, snapshot.Invoices);
    }

    [Fact]
    public async Task Accepts_payer_action_required_when_the_link_is_sandbox()
    {
        await using var host = PayPalHost();
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.CreateStatus = "PAYER_ACTION_REQUIRED";
        var started = await StartAsync(host, "PP-PAYER", 5);

        Assert.StartsWith("https://www.sandbox.paypal.com/", started.Payment.CheckoutUrl, StringComparison.Ordinal);
        Assert.Equal("GET", started.Payment.RedirectMethod);
        Assert.Equal(1, script.CreateCalls);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Payments.SingleAsync(item => item.OrderId == started.Payment.OrderId);
        Assert.Equal(PayPalCreateOutcome.Identified, payment.ExternalCreateOutcome);
        Assert.False(string.IsNullOrWhiteSpace(payment.ExternalOperationId));
    }

    [Fact]
    public async Task Stores_the_paypal_id_and_rejects_a_live_link_without_creating_another_order()
    {
        await using var host = PayPalHost();
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.CreateStatus = "PAYER_ACTION_REQUIRED";
        script.ApproveUrlOverride = "https://www.paypal.com/checkoutnow?token=LIVE";
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-LIVE-URL", ProductKind.Wine, initialStock: 5, vintage: "2024");
        const string key = "pp-live-url";
        var client = host.CreateClient();
        var response = await PayAsync(client, wine.Id, "PayPal", key);
        var body = await response.Content.ReadAsStringAsync();
        var repeated = await PayAsync(client, wine.Id, "PayPal", key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Contains(PayPalRules.ApprovalMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("checkoutnow?token=LIVE", body, StringComparison.Ordinal);
        Assert.Equal(1, script.CreateCalls);
        Assert.True(script.GetCalls >= 1);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.Payments.SingleAsync();
        Assert.Equal(script.PaypalOrderId, payment.ExternalOperationId);
        Assert.Null(payment.CheckoutUrl);
        Assert.Equal(4, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Leaves_an_approved_order_unpaid_until_capture()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-APPROVED", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        var client = host.CreateClient();
        var outcome = await OutcomeAsync(client, started.Payment);
        await using var scope = host.Services.CreateAsyncScope();
        var evidence = await scope.ServiceProvider.GetServices<IPaymentStatusQuery>()
            .Single(query => query.Provider == PaymentProvider.PayPal)
            .InspectAsync(started.Payment.OrderId);

        Assert.Equal(nameof(OrderStatus.PendingPayment), (await outcome.Content.ReadFromJsonAsync<CheckoutOutcomeDto>(JsonOptions()))!.Status);
        Assert.Equal(PaymentEvidence.StillPayable, evidence);
        Assert.Equal(0, script.CaptureCalls);
        var snapshot = await SnapshotAsync(host, started);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
    }

    [Fact]
    public async Task Captures_a_matching_payment_once()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-CAPTURE", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "APPROVED";
        var client = host.CreateClient();

        var first = await CaptureAsync(client, started.Payment);
        var second = await CaptureAsync(client, started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(nameof(OrderStatus.Paid), (await first.Content.ReadFromJsonAsync<CheckoutOutcomeDto>(JsonOptions()))!.Status);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(PaymentStatus.Succeeded, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Equal(0, snapshot.Invoices);
        Assert.Equal(1, snapshot.Confirmations);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CaptureId));
    }

    [Fact]
    public async Task Does_not_capture_again_when_two_returns_arrive_together()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-RACE", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "APPROVED";
        var client = host.CreateClient();

        var both = await Task.WhenAll(
            CaptureAsync(client, started.Payment),
            CaptureAsync(client, started.Payment));
        var snapshot = await SnapshotAsync(host, started);

        Assert.All(both, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Equal(1, snapshot.Confirmations);
    }

    [Fact]
    public async Task Does_not_capture_when_the_payer_has_not_approved()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-WAIT", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "PAYER_ACTION_REQUIRED";

        var response = await CaptureAsync(host.CreateClient(), started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(nameof(OrderStatus.PendingPayment), (await response.Content.ReadFromJsonAsync<CheckoutOutcomeDto>(JsonOptions()))!.Status);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Equal(0, snapshot.Confirmations);
    }

    [Fact]
    public async Task Confirms_a_held_reservation_when_capture_arrives_after_the_clock()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-LATE", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "APPROVED";
        var createdAt = DateTimeOffset.UtcNow;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expires = createdAt.AddMinutes(-5);
            var orderCreated = expires.AddMinutes(-30);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {orderCreated}, "ReservationExpiresAt" = {expires} WHERE "Id" = {started.Payment.OrderId}""");
        }

        var response = await CaptureAsync(host.CreateClient(), started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Equal(1, snapshot.Confirmations);
    }

    [Fact]
    public async Task Confirms_on_return_and_ignores_the_capture_webhook_that_arrives_later()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-RETURN", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        using var rsa = Certificate(script);
        var client = host.CreateClient();

        var returned = await CaptureAsync(client, started.Payment);
        var completed = Sign("WH-AFTER-RETURN", "PAYMENT.CAPTURE.COMPLETED", script.PaypalOrderId, rsa);
        var webhook = await PostWebhookAsync(client, completed);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, script.CaptureCalls);
        Assert.Equal(1, snapshot.Confirmations);
    }

    [Fact]
    public async Task Releases_stock_when_the_capture_is_declined()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-DENIED", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        script.NextCaptureStatus = "DECLINED";

        var response = await CaptureAsync(host.CreateClient(), started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, snapshot.Status);
        Assert.Equal(PaymentStatus.Failed, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Released, snapshot.Reservation);
        Assert.Equal(5, snapshot.Stock);
        Assert.Equal(0, snapshot.Invoices);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Keeps_stock_when_the_amount_or_currency_does_not_match(bool wrongCurrency)
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, wrongCurrency ? "PP-CUR" : "PP-AMT", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        if (wrongCurrency)
        {
            script.CurrencyOverride = "USD";
        }
        else
        {
            script.AmountOverride = started.Payment.TotalCents + 1;
        }

        var response = await CaptureAsync(host.CreateClient(), started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
        Assert.Contains(wrongCurrency ? PayPalRules.CurrencyMessage : PayPalRules.AmountMessage, snapshot.Attention);
    }

    [Fact]
    public async Task Keeps_the_reservation_when_capture_loses_the_network_and_confirms_on_retry()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-NET", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        script.ThrowOnCapture = true;
        var client = host.CreateClient();

        var failed = await CaptureAsync(client, started.Payment);
        var during = await SnapshotAsync(host, started);
        script.ThrowOnCapture = false;
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        var recovered = await CaptureAsync(client, started.Payment);
        var after = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains(PayPalRules.TransportMessage, await failed.Content.ReadAsStringAsync());
        Assert.Equal(OrderStatus.PendingPayment, during.Status);
        Assert.Equal(4, during.Stock);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(OrderStatus.Paid, after.Status);
        Assert.Equal(4, after.Stock);
        Assert.Equal(1, script.CaptureCalls);
    }

    [Fact]
    public async Task Confirms_from_a_verified_webhook_and_ignores_a_duplicate_and_a_later_approval()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-HOOK", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        using var rsa = Certificate(script);
        var client = host.CreateClient();
        var completed = Sign("WH-CAPTURE-1", "PAYMENT.CAPTURE.COMPLETED", script.PaypalOrderId, rsa);
        var duplicate = await PostWebhookAsync(client, completed);
        var again = await PostWebhookAsync(client, completed);
        var approved = Sign("WH-APPROVED-LATER", "CHECKOUT.ORDER.APPROVED", script.PaypalOrderId, rsa);
        var late = await PostWebhookAsync(client, approved);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(2, snapshot.Events);
        Assert.Equal(1, snapshot.Confirmations);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Rejects_a_webhook_with_an_invalid_signature_without_touching_stock()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-SIGN", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        using var rsa = Certificate(script);
        var signed = Sign("WH-BAD", "PAYMENT.CAPTURE.COMPLETED", script.PaypalOrderId, rsa, valid: false);

        var response = await PostWebhookAsync(host.CreateClient(), signed);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, snapshot.Events);
    }

    [Fact]
    public async Task Captures_from_an_approval_webhook_that_arrives_before_the_browser()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-ORDER", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        using var rsa = Certificate(script);
        var approved = Sign("WH-APPROVED-1", "CHECKOUT.ORDER.APPROVED", script.PaypalOrderId, rsa);
        var captured = Sign("WH-CAPTURE-2", "PAYMENT.CAPTURE.COMPLETED", script.PaypalOrderId, rsa);

        var first = await PostWebhookAsync(host.CreateClient(), approved);
        var middle = await SnapshotAsync(host, started);
        var second = await PostWebhookAsync(host.CreateClient(), captured);
        var after = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(OrderStatus.Paid, middle.Status);
        Assert.Equal(OrderStatus.Paid, after.Status);
        Assert.Equal(4, after.Stock);
        Assert.Equal(1, script.CaptureCalls);
        Assert.Equal(2, after.Events);
    }

    [Fact]
    public async Task Does_not_release_an_expired_paypal_hold_while_the_order_can_still_be_captured()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-HOLD", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "APPROVED";
        var createdAt = DateTimeOffset.UtcNow;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expires = createdAt.AddMinutes(-5);
            var orderCreated = expires.AddMinutes(-30);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {orderCreated}, "ReservationExpiresAt" = {expires} WHERE "Id" = {started.Payment.OrderId}""");
        }

        await using var sweepScope = host.Services.CreateAsyncScope();
        var sweep = await sweepScope.ServiceProvider.GetRequiredService<IReservationSweep>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(0, sweep.Released);
        Assert.Equal(0, sweep.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
    }

    [Fact]
    public async Task Keeps_stripe_and_redsys_available_beside_paypal_sandbox()
    {
        await using var host = PayPalHost(redsys: true);
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-MIX", ProductKind.Wine, initialStock: 8, vintage: "2024");
        var client = host.CreateClient();
        var listed = await client.GetFromJsonAsync<List<PaymentProviderOfferDto>>("/api/v1/checkout/payment-providers");
        var stripe = await PayAsync(client, wine.Id, "Stripe", "pp-stripe-" + Guid.NewGuid().ToString("N"));
        var redsys = await PayAsync(client, wine.Id, "Redsys", "pp-redsys-" + Guid.NewGuid().ToString("N"));
        var paypal = await PayAsync(client, wine.Id, "PayPal", "pp-paypal-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(["PayPal", "Redsys", "Stripe"], listed!.Select(offer => offer.Code).OrderBy(code => code).ToArray());
        Assert.Equal(HttpStatusCode.OK, stripe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, redsys.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paypal.StatusCode);
        Assert.Equal("Stripe", (await ReadAsync(stripe)).Provider);
        Assert.Equal("Redsys", (await ReadAsync(redsys)).Provider);
        Assert.Equal("PayPal", (await ReadAsync(paypal)).Provider);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(3, await db.Orders.CountAsync());
        Assert.Equal(5, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Does_not_offer_paypal_live()
    {
        await using var host = PayPalHost(environment: "Live");
        var client = host.CreateClient();
        var listed = await client.GetFromJsonAsync<List<PaymentProviderOfferDto>>("/api/v1/checkout/payment-providers");
        var response = await PayAsync(client, Guid.NewGuid(), "PayPal", "pp-live-" + Guid.NewGuid().ToString("N"));

        Assert.DoesNotContain(listed!, offer => offer.Code == "PayPal");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Does_not_repeat_create_when_the_response_is_lost_inside_or_outside_the_documented_window()
    {
        await using var host = PayPalHost();
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.LoseCreateResponse = true;
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-LOST", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var key = "pp-lost-" + Guid.NewGuid().ToString("N");
        var client = host.CreateClient();
        var lost = await PayAsync(client, wine.Id, "PayPal", key);
        var inside = await PayAsync(client, wine.Id, "PayPal", key);
        var insideBody = await inside.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, lost.StatusCode);
        Assert.Contains(PayPalRules.TransportMessage, await lost.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, inside.StatusCode);
        Assert.Contains(PayPalRules.UncertainCreateMessage, insideBody, StringComparison.Ordinal);
        Assert.Equal(1, script.CreateCalls);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Payments.SingleAsync();
            var attempted = DateTimeOffset.UtcNow.AddHours(-7);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Payments" SET "ExternalCreateAttemptedAt" = {attempted} WHERE "Id" = {payment.Id}""");
        }

        var outside = await PayAsync(client, wine.Id, "PayPal", key);
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Contains(PayPalRules.ExpiredIdempotencyMessage, await outside.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, script.CreateCalls);
        Assert.Equal(0, script.GetCalls);
        await using var check = host.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await database.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
        Assert.Null(await database.Payments.Select(item => item.ExternalOperationId).SingleAsync());
    }

    [Fact]
    public async Task Does_not_create_again_for_a_legacy_failure_without_an_external_id()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-LEGACY", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.Include(item => item.Payments).ThenInclude(item => item.Events).SingleAsync(item => item.Id == started.Payment.OrderId);
            var payment = order.Payments.Single();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Payments"
                SET "ExternalOperationId" = NULL,
                    "CheckoutUrl" = NULL,
                    "CheckoutExpiresAt" = NULL,
                    "ExternalCreateOutcome" = NULL,
                    "ExternalRequestId" = NULL,
                    "ExternalRequestFingerprint" = NULL,
                    "ExternalCreateAttemptedAt" = NULL
                WHERE "Id" = {payment.Id}
                """);
            db.ChangeTracker.Clear();
            order = await db.Orders.Include(item => item.Payments).ThenInclude(item => item.Events).SingleAsync(item => item.Id == started.Payment.OrderId);
            payment = order.Payments.Single();
            var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), PayPalCreateMarkers.EventId(payment.Id), "paypal.order.create", DateTimeOffset.UtcNow);
            order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, DateTimeOffset.UtcNow, PayPalRules.ApprovalMessage);
            await db.SaveChangesAsync();
        }

        var repeated = await PayAsync(host.CreateClient(), started.ProductId, "PayPal", started.Key);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Contains(PayPalRules.UncertainCreateMessage, await repeated.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, script.CreateCalls);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
    }

    [Fact]
    public async Task Does_not_let_another_provider_release_a_historical_paypal_hold()
    {
        await using var host = PayPalHost();
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-OTHER", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var key = "pp-other-" + Guid.NewGuid().ToString("N");
        var client = host.CreateClient();
        var started = await PayAsync(client, wine.Id, "PayPal", key);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.Include(item => item.Payments).ThenInclude(item => item.Events).SingleAsync(item => item.IdempotencyKey == key);
            var payment = order.Payments.Single();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Payments"
                SET "ExternalOperationId" = NULL,
                    "CheckoutUrl" = NULL,
                    "CheckoutExpiresAt" = NULL,
                    "ExternalCreateOutcome" = NULL,
                    "ExternalRequestId" = NULL,
                    "ExternalRequestFingerprint" = NULL,
                    "ExternalCreateAttemptedAt" = NULL
                WHERE "Id" = {payment.Id}
                """);
            db.ChangeTracker.Clear();
            order = await db.Orders.Include(item => item.Payments).ThenInclude(item => item.Events).SingleAsync(item => item.Id == order.Id);
            payment = order.Payments.Single();
            var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), PayPalCreateMarkers.EventId(payment.Id), "paypal.order.create", DateTimeOffset.UtcNow);
            order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, DateTimeOffset.UtcNow, PayPalRules.ApprovalMessage);
            await db.SaveChangesAsync();
        }

        var stripe = await PayAsync(client, wine.Id, "Stripe", key);
        await using var check = host.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await database.Orders.Include(item => item.Payments).SingleAsync(item => item.IdempotencyKey == key);

        Assert.Equal(HttpStatusCode.Conflict, stripe.StatusCode);
        Assert.Contains(ExternalHoldRules.UncertainHoldMessage, await stripe.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(OrderStatus.PendingPayment, stored.Status);
        Assert.Equal(PaymentStatus.Pending, Assert.Single(stored.Payments).Status);
        Assert.Equal(StockReservationStatus.Reserved, (await database.StockReservations.SingleAsync()).Status);
        Assert.Equal(4, await database.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
        Assert.Equal(1, await database.Orders.CountAsync());
    }

    [Fact]
    public async Task Keeps_the_reservation_when_an_uncertain_paypal_hold_has_expired()
    {
        await using var host = PayPalHost();
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.LoseCreateResponse = true;
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-UNCERTAIN-HOLD", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var key = "pp-uncertain-hold-" + Guid.NewGuid().ToString("N");
        var client = host.CreateClient();
        await PayAsync(client, wine.Id, "PayPal", key);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var orderId = await db.Orders.Where(item => item.IdempotencyKey == key).Select(item => item.Id).SingleAsync();
            var expires = DateTimeOffset.UtcNow.AddMinutes(-5);
            var created = expires.AddMinutes(-30);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {created}, "ReservationExpiresAt" = {expires} WHERE "Id" = {orderId}""");
        }

        var expired = await PayAsync(client, wine.Id, "PayPal", key);
        await using var check = host.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reservation = await database.StockReservations.SingleAsync();

        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Contains(PayPalRules.UncertainHoldMessage, await expired.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, script.CreateCalls);
        Assert.Equal(StockReservationStatus.Reserved, reservation.Status);
        Assert.Equal(4, await database.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Attaches_an_early_webhook_without_capturing_or_releasing_stock()
    {
        await using var host = PayPalHost();
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.LoseCreateResponse = true;
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-EARLY", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var key = "pp-early-" + Guid.NewGuid().ToString("N");
        var client = host.CreateClient();
        await PayAsync(client, wine.Id, "PayPal", key);
        Guid orderId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            orderId = await db.Orders.Where(item => item.IdempotencyKey == key).Select(item => item.Id).SingleAsync();
        }

        using var rsa = Certificate(script);
        var anonymous = Sign("WH-EARLY-ANON", "PAYMENT.CAPTURE.PENDING", script.PaypalOrderId, rsa, captureStatus: "PENDING");
        var named = Sign("WH-EARLY-NAMED", "PAYMENT.CAPTURE.PENDING", script.PaypalOrderId, rsa, captureStatus: "PENDING", customId: orderId);
        var ignored = await PostWebhookAsync(client, anonymous);
        var attached = await PostWebhookAsync(client, named);
        await using var check = host.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await database.Payments.SingleAsync();

        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        Assert.Equal(script.PaypalOrderId, payment.ExternalOperationId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(1, script.CreateCalls);
        Assert.Equal(0, script.CaptureCalls);
        Assert.Equal(4, await database.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Does_not_mark_paid_or_discount_stock_twice_when_local_confirmation_cannot_finish()
    {
        await using var host = PayPalHost();
        var started = await StartAsync(host, "PP-CONFIRM-FAIL", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var released = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StockReservations" SET "Status" = 'Released', "ReleasedAt" = {released} WHERE "OrderId" = {started.Payment.OrderId}""");
        }

        var client = host.CreateClient();
        var first = await CaptureAsync(client, started.Payment);
        var second = await CaptureAsync(client, started.Payment);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(4, snapshot.Stock);
        Assert.Contains(PayPalRules.StockMessage, snapshot.Attention);
        Assert.Equal(1, script.CaptureCalls);
    }

    [Fact]
    public async Task Serializes_two_create_attempts_into_one_paypal_order()
    {
        await using var host = PayPalHost();
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PP-RACE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var key = "pp-race-" + Guid.NewGuid().ToString("N");
        var client = host.CreateClient();
        var first = PayAsync(client, wine.Id, "PayPal", key);
        var second = PayAsync(client, wine.Id, "PayPal", key);
        var responses = await Task.WhenAll(first, second);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, script.CreateCalls);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(4, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    private async Task<StartedPayPal> StartAsync(WebApplicationFactory<Program> host, string reference, int stock)
    {
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, initialStock: stock, vintage: "2024");
        var key = reference + "-" + Guid.NewGuid().ToString("N");
        var response = await PayAsync(host.CreateClient(), wine.Id, "PayPal", key);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = JsonSerializer.Deserialize<CheckoutPaymentDto>(body, JsonOptions())!;
        return new StartedPayPal(payment, wine.Id, key, body);
    }

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid productId, string? provider, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(new PlaceGuestOrderRequest(
                [new CheckoutLineRequest(productId, 1)],
                new CheckoutDestinationRequest("37001", "ES"),
                "Ana Rivas",
                "ana@example.com",
                "+34600111222",
                "Calle Mayor 1",
                "Salamanca",
                "Salamanca",
                null,
                provider))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CaptureAsync(HttpClient client, CheckoutPaymentDto payment)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/paypal/capture")
        {
            Content = JsonContent.Create(new PayPalCaptureRequest(payment.OrderId))
        };
        request.Headers.TryAddWithoutValidation("Checkout-Access-Token", payment.CheckoutAccessToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> OutcomeAsync(HttpClient client, CheckoutPaymentDto payment)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/outcome")
        {
            Content = JsonContent.Create(new CheckoutOutcomeRequest(payment.OrderId))
        };
        request.Headers.TryAddWithoutValidation("Checkout-Access-Token", payment.CheckoutAccessToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, SignedWebhook signed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/paypal/webhook")
        {
            Content = new StringContent(signed.Body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("paypal-transmission-id", signed.TransmissionId);
        request.Headers.TryAddWithoutValidation("paypal-transmission-time", signed.TransmissionTime);
        request.Headers.TryAddWithoutValidation("paypal-transmission-sig", signed.Signature);
        request.Headers.TryAddWithoutValidation("paypal-cert-url", CertUrl);
        request.Headers.TryAddWithoutValidation("paypal-auth-algo", PayPalRules.Algorithm);
        return await client.SendAsync(request);
    }

    private static SignedWebhook Sign(
        string eventId,
        string eventType,
        string paypalOrderId,
        RSA rsa,
        bool valid = true,
        string captureStatus = "COMPLETED",
        Guid? customId = null)
    {
        var body = eventType.StartsWith("CHECKOUT.ORDER.", StringComparison.Ordinal)
            ? "{\"id\":\"" + eventId + "\",\"event_type\":\"" + eventType + "\",\"resource\":{\"id\":\"" + paypalOrderId + "\",\"status\":\"APPROVED\"}}"
            : "{\"id\":\"" + eventId + "\",\"event_type\":\"" + eventType + "\",\"resource\":{\"id\":\"3C679366HH908993F\",\"status\":\"" + captureStatus + "\"" + (customId is Guid id ? ",\"custom_id\":\"" + id.ToString("D") + "\"" : string.Empty) + ",\"supplementary_data\":{\"related_ids\":{\"order_id\":\"" + paypalOrderId + "\"}}}}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var transmissionId = "tx-" + eventId;
        const string transmissionTime = "2026-10-14T08:00:01Z";
        var message = PayPalProtocol.Message(transmissionId, transmissionTime, WebhookId, bytes);
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        if (!valid)
        {
            signature = Convert.ToBase64String(Enumerable.Repeat((byte)7, 64).ToArray());
        }

        return new SignedWebhook(body, transmissionId, transmissionTime, signature);
    }

    private static RSA Certificate(PayPalOrderScript script)
    {
        var rsa = RSA.Create(2048);
        var certificate = new CertificateRequest("CN=paypal-sandbox-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        script.CertificatePem = certificate.ExportCertificatePem();
        return rsa;
    }

    private static async Task<PayPalSnapshot> SnapshotAsync(WebApplicationFactory<Program> host, StartedPayPal started)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).ThenInclude(payment => payment.Events).SingleAsync(item => item.Id == started.Payment.OrderId);
        var payment = order.Payments.Single();
        var reservation = await db.StockReservations.SingleAsync(item => item.OrderId == order.Id);
        var stock = await db.Stocks.Where(item => item.ProductId == started.ProductId).Select(item => item.Quantity).SingleAsync();
        return new PayPalSnapshot(
            order.Status,
            payment.Status,
            reservation.Status,
            stock,
            payment.Events.Count(item => item.ProcessedAt is not null),
            await db.Invoices.CountAsync(),
            payment.AmountCents,
            payment.ExternalCaptureId,
            order.ReservationExpiresAt - order.CreatedAt,
            payment.Events.Select(item => item.AttentionReason).Where(item => item is not null).Cast<string>().ToArray(),
            await db.OrderNotifications.CountAsync(item => item.OrderId == order.Id && item.Kind == OrderNotificationKind.PaidConfirmation));
    }

    private static async Task<CheckoutPaymentDto> ReadAsync(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<CheckoutPaymentDto>(JsonOptions()))!;
    }

    private WebApplicationFactory<Program> PayPalHost(string environment = "Sandbox", bool redsys = false)
    {
        return Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Stripe:Gateway", "Stub");
            builder.UseSetting("PayPal:Enabled", "true");
            builder.UseSetting("PayPal:Environment", environment);
            builder.UseSetting("PayPal:Gateway", "Script");
            builder.UseSetting("PayPal:ClientId", "sandbox-client-id");
            builder.UseSetting("PayPal:ClientSecret", Secret);
            builder.UseSetting("PayPal:WebhookId", WebhookId);
            builder.UseSetting("PayPal:ReturnUrl", "http://localhost:4200/checkout/exito");
            builder.UseSetting("PayPal:CancelUrl", "http://localhost:4200/checkout/cancelado");
            if (!redsys)
            {
                return;
            }

            builder.UseSetting("Redsys:Enabled", "true");
            builder.UseSetting("Redsys:Environment", "Test");
            builder.UseSetting("Redsys:MerchantCode", "999008881");
            builder.UseSetting("Redsys:Terminal", "001");
            builder.UseSetting("Redsys:SignatureVersion", RedsysSignature.Version);
            builder.UseSetting("Redsys:SecretKey", "sq7HjrUOBfKmC576ILgskD5srU870gJ7");
            builder.UseSetting("Redsys:MerchantUrl", "https://localhost/api/v1/checkout/redsys/notification");
            builder.UseSetting("Redsys:UrlOk", "http://localhost:4200/checkout/exito");
            builder.UseSetting("Redsys:UrlKo", "http://localhost:4200/checkout/cancelado");
        });
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private sealed record StartedPayPal(CheckoutPaymentDto Payment, Guid ProductId, string Key, string Body);

    private sealed record SignedWebhook(string Body, string TransmissionId, string TransmissionTime, string Signature);

    private sealed record PayPalSnapshot(
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        StockReservationStatus Reservation,
        int Stock,
        int Events,
        int Invoices,
        long AmountCents,
        string? CaptureId,
        TimeSpan Hold,
        IReadOnlyList<string> Attention,
        int Confirmations);
}
