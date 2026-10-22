using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.IntegrationTests;

/// <summary>
/// La conciliación lee PayPalOrderScript. No llama a api-m.sandbox.paypal.com y no cobra en Sandbox.
/// </summary>
public sealed class ProviderReconciliationTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const string Secret = "clave-paypal-local";
    private const string WebhookId = "8AB12345CD678901E";
    private const string CertUrl = "https://api.sandbox.paypal.com/v1/notifications/certs/CERT-LOCAL";

    [Fact]
    public async Task Stays_disabled_and_confirms_a_paid_paypal_order_without_a_new_capture()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-PAID", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        await using var scope = host.Services.CreateAsyncScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IOptions<ReservationSweepOptions>>().Value.ProviderReconciliationEnabled);
        Assert.False(scope.ServiceProvider.GetRequiredService<IOptions<ReservationSweepOptions>>().Value.HoldAlertEnabled);
        Assert.False(scope.ServiceProvider.GetRequiredService<IOptions<ReservationSweepOptions>>().Value.SweepEnabled);

        var result = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(0, result.Released);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(PaymentStatus.Succeeded, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
        Assert.True(script.GetCalls >= 1);
        Assert.Equal(1, snapshot.Sales);
    }

    [Fact]
    public async Task Releases_a_paypal_order_that_is_closed_without_payment()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-VOID", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "VOIDED";

        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(1, result.Released);
        Assert.Equal(OrderStatus.Cancelled, snapshot.Status);
        Assert.Equal(PaymentStatus.Failed, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Released, snapshot.Reservation);
        Assert.Equal(5, snapshot.Stock);
        Assert.Equal(0, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Equal(1, snapshot.Cancellations);
    }

    [Theory]
    [InlineData("CREATED")]
    [InlineData("SAVED")]
    [InlineData("PAYER_ACTION_REQUIRED")]
    [InlineData("APPROVED")]
    public async Task Keeps_stock_while_paypal_has_no_completed_capture(string status)
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-OPEN", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = status;

        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(0, result.Released);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Keeps_stock_when_a_paypal_capture_is_still_pending()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-PEND", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        script.NextCaptureStatus = "PENDING";
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(0, result.Released);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Keeps_stock_when_paypal_does_not_answer()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-DOWN", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().ThrowOnGet = true;
        await using var scope = host.Services.CreateAsyncScope();
        var failed = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(1, failed.Failed);
        Assert.Equal(0, failed.Released);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
    }

    [Fact]
    public async Task Confirms_a_late_paypal_payment_after_the_local_window()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-LATE", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        var expires = DateTimeOffset.UtcNow.AddMinutes(-40);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {expires.AddMinutes(-30)}, "ReservationExpiresAt" = {expires} WHERE "Id" = {started.OrderId}""");
        }

        await using var run = host.Services.CreateAsyncScope();
        var result = await run.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Opens_a_critical_incident_when_the_payment_can_no_longer_be_settled()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-CRIT", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        await using (var release = host.Services.CreateAsyncScope())
        {
            await release.ServiceProvider.GetRequiredService<IOrderStock>().ReleaseAsync(started.OrderId, DateTimeOffset.UtcNow);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var incident = await db.ReservationHoldIncidents.SingleAsync(item => item.OrderId == started.OrderId);

        Assert.Equal(1, result.Incidents);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(5, snapshot.Stock);
        Assert.Equal(1, snapshot.Sales);
        Assert.Equal(nameof(ReservationHoldSeverity.Critical), incident.Severity);
        Assert.Equal(nameof(ReservationHoldReconciliation.PaidUnsettled), incident.Reconciliation);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal(script.PaypalOrderId, incident.ExternalReference);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Confirms_a_paypal_pack_once_and_ignores_a_repeated_pass()
    {
        await using var host = Host();
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RC-WINE", ProductKind.Wine, initialStock: 8, vintage: "2024");
        var pack = await harness.CreateProductAsync(categoryId, "RC-PACK", ProductKind.Pack, componentsJson: Components(wine.Id, 2));
        var response = await PayAsync(host.CreateClient(), pack.Id, "PayPal", "rc-pack-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = (await response.Content.ReadFromJsonAsync<CheckoutPaymentDto>(JsonOptions()))!;
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;

        await using var firstScope = host.Services.CreateAsyncScope();
        await firstScope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var once = await SnapshotAsync(host, payment.OrderId, wine.Id);
        await using var secondScope = host.Services.CreateAsyncScope();
        await secondScope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var twice = await SnapshotAsync(host, payment.OrderId, wine.Id);

        Assert.Equal(OrderStatus.Paid, twice.Status);
        Assert.Equal(6, once.Stock);
        Assert.Equal(6, twice.Stock);
        Assert.Equal(1, once.Sales);
        Assert.Equal(1, twice.Sales);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Does_not_query_paypal_again_until_the_saved_backoff_and_survives_a_new_scope()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-WAIT", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "APPROVED";
        var now = DateTimeOffset.UtcNow;

        await using (var first = host.Services.CreateAsyncScope())
        {
            await first.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync(now);
        }

        var afterFirst = script.GetCalls;
        await using (var restarted = host.Services.CreateAsyncScope())
        {
            var skipped = await restarted.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync(now);
            Assert.Equal(0, skipped.Processed);
        }

        await using var later = host.Services.CreateAsyncScope();
        var retried = await later.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync(now.AddMinutes(16));
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(1, afterFirst);
        Assert.Equal(2, script.GetCalls);
        Assert.Equal(1, retried.Processed);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, script.CaptureCalls);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
    }

    [Fact]
    public async Task Applies_one_stock_movement_when_reconciliation_races_a_webhook()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-RACE", 5);
        var script = host.Services.GetRequiredService<PayPalOrderScript>();
        script.Status = "COMPLETED";
        script.IncludeCapture = true;
        using var rsa = Certificate(script);
        var signed = Sign("WH-RACE-1", "PAYMENT.CAPTURE.COMPLETED", script.PaypalOrderId, rsa);

        var reconcile = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        });
        var webhook = PostWebhookAsync(host.CreateClient(), signed);
        await Task.WhenAll(reconcile, webhook);
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);

        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, snapshot.Sales);
        Assert.Equal(0, snapshot.Cancellations);
        Assert.Equal(0, script.CaptureCalls);
    }

    [Fact]
    public async Task Keeps_a_silent_redsys_operation_and_opens_a_hold_only_after_24_hours()
    {
        await using var host = Host();
        var young = await StartRedsysAsync(host, "RC-YOUNG", 6);
        var old = await StartRedsysAsync(host, "RC-OLD", 6);
        var past = DateTimeOffset.UtcNow.AddHours(-25);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StockReservations" SET "ReservedAt" = {past} WHERE "OrderId" = {old.OrderId}""");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {past.AddMinutes(-5)}, "ReservationExpiresAt" = {past.AddMinutes(30)} WHERE "Id" = {old.OrderId}""");
        }

        await using var run = host.Services.CreateAsyncScope();
        var result = await run.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync();
        var dbAfter = run.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var youngSnapshot = await SnapshotAsync(host, young.OrderId, young.ProductId);
        var oldSnapshot = await SnapshotAsync(host, old.OrderId, old.ProductId);
        var incidents = await dbAfter.ReservationHoldIncidents.Where(item => item.ResolvedAt == null).ToListAsync();
        var states = await dbAfter.ProviderReconciliations.ToListAsync();

        Assert.Equal(0, result.Released);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(OrderStatus.PendingPayment, youngSnapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, youngSnapshot.Reservation);
        Assert.Equal(5, youngSnapshot.Stock);
        Assert.Equal(OrderStatus.PendingPayment, oldSnapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, oldSnapshot.Reservation);
        Assert.Equal(5, oldSnapshot.Stock);
        Assert.DoesNotContain(incidents, item => item.OrderId == young.OrderId);
        var held = Assert.Single(incidents, item => item.OrderId == old.OrderId);
        Assert.Equal(nameof(ReservationHoldSeverity.Held), held.Severity);
        Assert.Equal(nameof(ReservationHoldReconciliation.Holding), held.Reconciliation);
        Assert.Null(held.ResolvedAt);
        Assert.Equal(old.Operation, held.ExternalReference);
        Assert.All(states, item => Assert.Equal(nameof(PaymentEvidence.Unknown), item.Evidence));
        Assert.DoesNotContain(Secret, held.ExternalReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_pending_operations_and_records_an_admin_paypal_recheck_without_releasing_stock()
    {
        await using var host = Host();
        var started = await StartPayPalAsync(host, "RC-ADMIN", 5);
        host.Services.GetRequiredService<PayPalOrderScript>().Status = "APPROVED";
        await TestCatalog.SeedAsync();
        using var client = Browser(host);
        await LoginAsync(client, TestAccounts.AdminUsername);

        var page = await client.GetAsync("/admin/payments/reconciliation");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var recheck = await client.PostAsync(
            "/admin/payments/reconciliation?handler=Recheck&orderId=" + started.OrderId,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        var snapshot = await SnapshotAsync(host, started.OrderId, started.ProductId);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await db.PaymentEvents.Select(item => item.EventType).ToListAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(started.Number, html, StringComparison.Ordinal);
        Assert.Contains(host.Services.GetRequiredService<PayPalOrderScript>().PaypalOrderId, html, StringComparison.Ordinal);
        Assert.Contains("no se libera por el tiempo", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Marcar como cobrado", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Liberar stock", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, recheck.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, host.Services.GetRequiredService<PayPalOrderScript>().CaptureCalls);
        Assert.Contains(ProviderReconciliationRules.AdminEventType, events);
    }

    private async Task<Started> StartPayPalAsync(WebApplicationFactory<Program> host, string reference, int stock)
    {
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, initialStock: stock, vintage: "2024");
        var response = await PayAsync(host.CreateClient(), wine.Id, "PayPal", reference + "-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = (await response.Content.ReadFromJsonAsync<CheckoutPaymentDto>(JsonOptions()))!;
        return new Started(payment.OrderId, payment.Number, wine.Id, null);
    }

    private async Task<Started> StartRedsysAsync(WebApplicationFactory<Program> host, string reference, int stock)
    {
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, initialStock: stock, vintage: "2024");
        var response = await PayAsync(host.CreateClient(), wine.Id, "Redsys", reference + "-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = (await response.Content.ReadFromJsonAsync<CheckoutPaymentDto>(JsonOptions()))!;
        using var document = RedsysSignature.DecodeParameters(payment.RedirectFields!["Ds_MerchantParameters"]);
        return new Started(payment.OrderId, payment.Number, wine.Id, RedsysParameterReader.Read(document.RootElement).Order);
    }

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid productId, string provider, string key)
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

    private static SignedWebhook Sign(string eventId, string eventType, string paypalOrderId, RSA rsa)
    {
        var body = "{\"id\":\"" + eventId + "\",\"event_type\":\"" + eventType + "\",\"resource\":{\"id\":\"3C679366HH908993F\",\"status\":\"COMPLETED\",\"supplementary_data\":{\"related_ids\":{\"order_id\":\"" + paypalOrderId + "\"}}}}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var transmissionId = "tx-" + eventId;
        const string transmissionTime = "2026-10-20T08:00:01Z";
        var message = PayPalProtocol.Message(transmissionId, transmissionTime, WebhookId, bytes);
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
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

    private static async Task<PaymentSnapshot> SnapshotAsync(WebApplicationFactory<Program> host, Guid orderId, Guid productId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        var payment = order.Payments.Single();
        var reservation = await db.StockReservations.SingleAsync(item => item.OrderId == order.Id);
        var stock = await db.Stocks.SingleAsync(item => item.ProductId == productId);
        var sales = await db.StockMovements.CountAsync(item => item.StockId == stock.Id && item.Type == StockMovementType.Sale);
        var cancellations = await db.StockMovements.CountAsync(item => item.StockId == stock.Id && item.Type == StockMovementType.Cancellation);
        return new PaymentSnapshot(order.Status, payment.Status, reservation.Status, stock.Quantity, sales, cancellations);
    }

    private WebApplicationFactory<Program> Host()
    {
        return Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Stripe:Gateway", "Stub");
            builder.UseSetting("PayPal:Enabled", "true");
            builder.UseSetting("PayPal:Environment", "Sandbox");
            builder.UseSetting("PayPal:Gateway", "Script");
            builder.UseSetting("PayPal:ClientId", "sandbox-client-id");
            builder.UseSetting("PayPal:ClientSecret", Secret);
            builder.UseSetting("PayPal:WebhookId", WebhookId);
            builder.UseSetting("PayPal:ReturnUrl", "http://localhost:4200/checkout/exito");
            builder.UseSetting("PayPal:CancelUrl", "http://localhost:4200/checkout/cancelado");
            builder.UseSetting("Redsys:Enabled", "true");
            builder.UseSetting("Redsys:Environment", "Test");
            builder.UseSetting("Redsys:MerchantCode", "999008881");
            builder.UseSetting("Redsys:Terminal", "001");
            builder.UseSetting("Redsys:SignatureVersion", RedsysSignature.Version);
            builder.UseSetting("Redsys:SecretKey", "sq7HjrUOBfKmC576ILgskD5srU870gJ7");
            builder.UseSetting("Redsys:MerchantUrl", "https://localhost/api/v1/checkout/redsys/notification");
            builder.UseSetting("Redsys:UrlOk", "http://localhost:4200/checkout/exito");
            builder.UseSetting("Redsys:UrlKo", "http://localhost:4200/checkout/cancelado");
            builder.UseSetting("Reservations:ProviderReconciliationEnabled", "false");
            builder.UseSetting("Reservations:HoldAlertEnabled", "false");
            builder.UseSetting("Reservations:SweepEnabled", "false");
        });
    }

    private static HttpClient Browser(WebApplicationFactory<Program> host)
    {
        var handler = new CookieHandler { InnerHandler = host.Server.CreateHandler() };
        return new HttpClient(handler) { BaseAddress = host.Server.BaseAddress };
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

    private static string Components(Guid productId, int quantity)
    {
        return JsonSerializer.Serialize(new[] { new { productId, quantity } });
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private sealed record Started(Guid OrderId, string Number, Guid ProductId, string? Operation);

    private sealed record SignedWebhook(string Body, string TransmissionId, string TransmissionTime, string Signature);

    private sealed record PaymentSnapshot(
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        StockReservationStatus Reservation,
        int Stock,
        int Sales,
        int Cancellations);
}
