using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class RedsysCheckoutTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private const string ExampleKey = "sq7HjrUOBfKmC576ILgskD5srU870gJ7";
    private const string Merchant = "999008881";

    [Fact]
    public async Task Starts_a_signed_test_redirect_and_a_retry_reuses_the_operation()
    {
        await using var host = RedsysHost();
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RDS-START", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = host.CreateClient();
        var listed = await client.GetFromJsonAsync<List<PaymentProviderOfferDto>>("/api/v1/checkout/payment-providers");
        Assert.Equal(["Redsys", "Stripe"], listed!.Select(offer => offer.Code).OrderBy(code => code).ToArray());

        var key = "rds-start-" + Guid.NewGuid().ToString("N");
        var first = await PayAsync(client, wine.Id, "Redsys", key);
        var second = await PayAsync(client, wine.Id, "Redsys", key);
        var stripe = await PayAsync(client, wine.Id, "Stripe", "rds-stripe-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Matches("^[0-9a-f]{32}$", first.Headers.GetValues("X-Checkout-Correlation").Single());
        Assert.Matches("^[0-9a-f]{32}$", second.Headers.GetValues("X-Checkout-Correlation").Single());
        Assert.NotEqual(
            first.Headers.GetValues("X-Checkout-Correlation").Single(),
            second.Headers.GetValues("X-Checkout-Correlation").Single());
        Assert.False(stripe.Headers.Contains("X-Checkout-Correlation"));
        Assert.Equal(HttpStatusCode.OK, stripe.StatusCode);
        var payment = await ReadPaymentAsync(first);
        var repeated = await ReadPaymentAsync(second);
        var stripePayment = await ReadPaymentAsync(stripe);
        Assert.Equal(payment.OrderId, repeated.OrderId);
        Assert.Equal("Redsys", payment.Provider);
        Assert.Equal("POST", payment.RedirectMethod);
        Assert.Equal(RedsysSignature.TestEndpoint, payment.CheckoutUrl);
        Assert.DoesNotContain("sis.redsys.es/sis", payment.CheckoutUrl, StringComparison.Ordinal);
        Assert.Equal(RedsysSignature.Version, payment.RedirectFields!["Ds_SignatureVersion"]);
        using var document = RedsysSignature.DecodeParameters(payment.RedirectFields["Ds_MerchantParameters"]);
        var signed = RedsysParameterReader.Read(document.RootElement);
        Assert.Equal(payment.TotalCents, signed.AmountCents);
        Assert.Equal(RedsysSignature.EuroCurrency, signed.Currency);
        Assert.Equal(Merchant, signed.MerchantCode);
        Assert.True(RedsysSignature.Matches(
            payment.RedirectFields["Ds_MerchantParameters"],
            signed.Order,
            ExampleKey,
            payment.RedirectFields["Ds_Signature"]));
        Assert.DoesNotContain(ExampleKey, await first.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == payment.OrderId);
        var stored = Assert.Single(order.Payments);
        Assert.Equal(signed.Order, stored.RedsysOrderNumber);
        Assert.Equal(signed.Order, stored.ExternalOperationId);
        Assert.Equal(order.TotalCents, stored.AmountCents);
        Assert.Equal(stored.RedsysOrderNumber, (await db.Payments.SingleAsync(item => item.OrderId == repeated.OrderId)).RedsysOrderNumber);
        Assert.Equal(3, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
        Assert.Equal(PaymentProvider.Stripe, (await db.Payments.SingleAsync(item => item.OrderId == stripePayment.OrderId)).Provider);
        Assert.Equal(2, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Does_not_emit_a_redirect_when_the_operation_is_not_committed()
    {
        await using var host = RedsysHost();
        host.Services.GetRequiredService<RedsysRedirectFailureProbe>().FailNext();
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "RDS-ROLL", ProductKind.Wine, initialStock: 5, vintage: "2024");

        var response = await PayAsync(host.CreateClient(), wine.Id, "Redsys", "rds-roll-" + Guid.NewGuid().ToString("N"));
        var body = await response.Content.ReadAsStringAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync();
        var reservation = await db.StockReservations.SingleAsync(item => item.OrderId == order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Ds_Signature", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Ds_MerchantParameters", body, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Empty(order.Payments);
        Assert.Equal(StockReservationStatus.Reserved, reservation.Status);
        Assert.Equal(4, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Confirms_a_matching_authorization_and_ignores_the_browser_return()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-OK", 5);
        var client = host.CreateClient();
        var outcome = await client.PostAsJsonAsync("/api/v1/checkout/outcome", new CheckoutOutcomeRequest(started.Payment.OrderId));
        Assert.Equal(HttpStatusCode.NotFound, outcome.StatusCode);

        var before = await SnapshotAsync(host, started);
        Assert.Equal(OrderStatus.PendingPayment, before.Status);
        Assert.Equal(4, before.Stock);

        var confirmed = await NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var after = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(OrderStatus.Paid, after.Status);
        Assert.Equal(PaymentStatus.Succeeded, after.PaymentStatus);
        Assert.Equal(StockReservationStatus.Confirmed, after.Reservation);
        Assert.Equal(4, after.Stock);
        Assert.Equal(0, after.Invoices);
        Assert.Equal(1, after.Confirmations);
        await using var invoiceScope = host.Services.CreateAsyncScope();
        var panel = await invoiceScope.ServiceProvider.GetRequiredService<IInvoiceIssuance>().DescribeAsync(started.Payment.OrderId);
        Assert.Equal(InvoiceIssuanceRules.UnspecifiedProviderMessage, panel!.Notice);
        Assert.False(panel.CanIssue);
    }

    [Fact]
    public async Task Rejects_invalid_or_manipulated_signatures_without_touching_stock()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-SIGN", 5);
        var client = host.CreateClient();
        var valid = Notice(started, "0000", started.Payment.TotalCents.ToString(), RedsysSignature.EuroCurrency, Merchant, "1");
        var broken = new Dictionary<string, string>(valid) { ["Ds_Signature"] = valid["Ds_Signature"] + "x" };
        var rewritten = RedsysSignature.EncodeParameters(new Dictionary<string, string> { ["Ds_Amount"] = "1", ["Ds_Order"] = started.Operation });
        var manipulated = new Dictionary<string, string>(valid) { ["Ds_MerchantParameters"] = rewritten };
        var legacy = new Dictionary<string, string>(valid) { ["Ds_SignatureVersion"] = "HMAC_SHA256_V1" };

        Assert.Equal(HttpStatusCode.BadRequest, (await PostNoticeAsync(client, broken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostNoticeAsync(client, manipulated)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostNoticeAsync(client, legacy)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/checkout/redsys/notification", JsonContent.Create(valid))).StatusCode);

        var snapshot = await SnapshotAsync(host, started);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(0, snapshot.Events);
    }

    [Fact]
    public async Task Keeps_the_reservation_when_the_signed_notice_does_not_match()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-MIS", 5);
        var client = host.CreateClient();

        var amount = await NotifyAsync(client, started, "0000", started.Payment.TotalCents - 1);
        var currency = await NotifyAsync(client, started, "0000", started.Payment.TotalCents, "840");
        var merchant = await NotifyAsync(client, started, "0000", started.Payment.TotalCents, merchant: "111111111");
        var unknown = await NotifyAsync(client, started, "SIS0001", started.Payment.TotalCents);

        Assert.Equal(HttpStatusCode.OK, amount.StatusCode);
        Assert.Equal(HttpStatusCode.OK, currency.StatusCode);
        Assert.Equal(HttpStatusCode.OK, merchant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var snapshot = await SnapshotAsync(host, started);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(4, snapshot.Events);
        Assert.Contains(RedsysNotificationRules.AmountMessage, snapshot.Attention);
        Assert.Contains(RedsysNotificationRules.CurrencyMessage, snapshot.Attention);
        Assert.Contains(RedsysNotificationRules.MerchantMessage, snapshot.Attention);
        Assert.Contains(RedsysNotificationRules.UnknownMessage, snapshot.Attention);
    }

    [Fact]
    public async Task A_signed_denial_releases_once_and_a_later_authorization_does_not_sell_again()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-NO", 5);
        var client = host.CreateClient();

        var denied = await NotifyAsync(client, started, "0184", started.Payment.TotalCents);
        var repeated = await NotifyAsync(client, started, "0184", started.Payment.TotalCents);
        var late = await NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, snapshot.Status);
        Assert.Equal(PaymentStatus.Failed, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Released, snapshot.Reservation);
        Assert.Equal(5, snapshot.Stock);
        Assert.Contains(RedsysNotificationRules.StockMessage, snapshot.Attention);
        Assert.Equal(2, snapshot.Events);
    }

    [Theory]
    [InlineData("0001")]
    [InlineData("0099")]
    [InlineData("0400")]
    [InlineData("0900")]
    [InlineData("SIS0099")]
    public async Task Ambiguous_signed_responses_keep_the_reservation(string response)
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-AMB", 5);
        var client = host.CreateClient();

        var first = await NotifyAsync(client, started, response, started.Payment.TotalCents);
        var duplicate = await NotifyAsync(client, started, response, started.Payment.TotalCents);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Contains(RedsysNotificationRules.UnknownMessage, snapshot.Attention);
        Assert.Equal(1, snapshot.Events);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.DoesNotContain(
            await db.ReservationHoldIncidents.Where(item => item.OrderId == started.Payment.OrderId).ToListAsync(),
            item => item.Reconciliation == "PaidUnsettled");
    }

    [Theory]
    [InlineData("0100")]
    [InlineData("0190")]
    public async Task An_unequivocal_denial_releases_stock_and_cancels_the_order_once(string response)
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-DENY", 5);
        var client = host.CreateClient();

        var denied = await NotifyAsync(client, started, response, started.Payment.TotalCents);
        var repeated = await NotifyAsync(client, started, response, started.Payment.TotalCents);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, snapshot.Status);
        Assert.Equal(PaymentStatus.Failed, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Released, snapshot.Reservation);
        Assert.Equal(5, snapshot.Stock);
        Assert.Equal(1, snapshot.Events);
    }

    [Theory]
    [InlineData("3", "0900")]
    [InlineData("9", "0400")]
    [InlineData("3", "0000")]
    public async Task Refund_and_cancellation_codes_do_not_settle_a_purchase(string transactionType, string response)
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-OTHER", 5);
        var notice = Notice(started, response, started.Payment.TotalCents.ToString(), RedsysSignature.EuroCurrency, Merchant, "1");
        using var document = RedsysSignature.DecodeParameters(notice["Ds_MerchantParameters"]);
        var fields = new Dictionary<string, string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            fields[property.Name] = property.Value.GetString() ?? property.Value.ToString();
        }

        fields["Ds_TransactionType"] = transactionType;
        var parameters = RedsysSignature.EncodeParameters(fields);
        var rewritten = new Dictionary<string, string>
        {
            ["Ds_SignatureVersion"] = RedsysSignature.Version,
            ["Ds_MerchantParameters"] = parameters,
            ["Ds_Signature"] = RedsysSignature.Sign(parameters, started.Operation, ExampleKey)
        };

        var result = await PostNoticeAsync(host.CreateClient(), rewritten);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(PaymentStatus.Pending, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
    }

    [Fact]
    public async Task Duplicate_and_concurrent_authorizations_confirm_stock_once()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-DUP", 5);
        var client = host.CreateClient();
        var first = NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var second = NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var results = await Task.WhenAll(first, second);
        var third = await NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var snapshot = await SnapshotAsync(host, started);

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(PaymentStatus.Succeeded, snapshot.PaymentStatus);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, snapshot.Events);
        Assert.Equal(1, snapshot.Confirmations);
    }

    [Fact]
    public async Task Ignores_an_unknown_operation_and_a_wrong_terminal_without_releasing_stock()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-UNK", 5);
        var client = host.CreateClient();
        var unknownParameters = RedsysSignature.EncodeParameters(new Dictionary<string, string>
        {
            ["Ds_Amount"] = started.Payment.TotalCents.ToString(),
            ["Ds_Currency"] = RedsysSignature.EuroCurrency,
            ["Ds_Order"] = "999988887777",
            ["Ds_MerchantCode"] = Merchant,
            ["Ds_Terminal"] = "1",
            ["Ds_Response"] = "0000",
            ["Ds_TransactionType"] = "0"
        });
        var unknownFields = new Dictionary<string, string>
        {
            ["Ds_SignatureVersion"] = RedsysSignature.Version,
            ["Ds_MerchantParameters"] = unknownParameters,
            ["Ds_Signature"] = RedsysSignature.Sign(unknownParameters, "999988887777", ExampleKey)
        };
        var terminal = await NotifyAsync(client, started, "0000", started.Payment.TotalCents, terminal: "002");
        var unknown = await PostNoticeAsync(client, unknownFields);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, terminal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment, snapshot.Status);
        Assert.Equal(StockReservationStatus.Reserved, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Contains(RedsysNotificationRules.MerchantMessage, snapshot.Attention);
        Assert.Equal(0, snapshot.Confirmations);
    }

    [Fact]
    public async Task Confirms_one_notice_and_one_email_after_the_reservation_clock()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-LATE", 5);
        var createdAt = DateTimeOffset.UtcNow;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expires = createdAt.AddMinutes(-5);
            var orderCreated = expires.AddMinutes(-30);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {orderCreated}, "ReservationExpiresAt" = {expires} WHERE "Id" = {started.Payment.OrderId}""");
        }

        var confirmed = await NotifyAsync(host.CreateClient(), started, "0000", started.Payment.TotalCents);
        var snapshot = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(OrderStatus.Paid, snapshot.Status);
        Assert.Equal(StockReservationStatus.Confirmed, snapshot.Reservation);
        Assert.Equal(4, snapshot.Stock);
        Assert.Equal(1, snapshot.Confirmations);
    }

    [Fact]
    public async Task A_database_failure_rolls_back_and_the_retry_confirms()
    {
        await using var host = RedsysHost();
        var started = await StartAsync(host, "RDS-DB", 5);
        host.Services.GetRequiredService<RedsysNotificationFailureProbe>().FailNext();
        var client = host.CreateClient();

        var failed = await NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var rolledBack = await SnapshotAsync(host, started);
        var retried = await NotifyAsync(client, started, "0000", started.Payment.TotalCents);
        var confirmed = await SnapshotAsync(host, started);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain(ExampleKey, await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(OrderStatus.PendingPayment, rolledBack.Status);
        Assert.Equal(PaymentStatus.Pending, rolledBack.PaymentStatus);
        Assert.Equal(StockReservationStatus.Reserved, rolledBack.Reservation);
        Assert.Equal(4, rolledBack.Stock);
        Assert.Equal(0, rolledBack.Events);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(OrderStatus.Paid, confirmed.Status);
        Assert.Equal(4, confirmed.Stock);
        Assert.Equal(StockReservationStatus.Confirmed, confirmed.Reservation);
    }

    [Fact]
    public async Task Does_not_announce_redsys_when_the_environment_is_live()
    {
        await using var host = RedsysHost(environment: "Live");
        var client = host.CreateClient();
        var listed = await client.GetFromJsonAsync<List<PaymentProviderOfferDto>>("/api/v1/checkout/payment-providers");
        Assert.Equal([nameof(PaymentProvider.Stripe)], listed!.Select(offer => offer.Code).ToArray());

        var response = await PayAsync(client, Guid.NewGuid(), "Redsys", "rds-live-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
    }

    private async Task<StartedPayment> StartAsync(WebApplicationFactory<Program> host, string reference, int stock)
    {
        var harness = new CatalogHarness(host.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, initialStock: stock, vintage: "2024");
        var response = await PayAsync(host.CreateClient(), wine.Id, "Redsys", reference + "-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await ReadPaymentAsync(response);
        using var document = RedsysSignature.DecodeParameters(payment.RedirectFields!["Ds_MerchantParameters"]);
        return new StartedPayment(payment, wine.Id, RedsysParameterReader.Read(document.RootElement).Order);
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

    private static async Task<HttpResponseMessage> NotifyAsync(
        HttpClient client,
        StartedPayment started,
        string response,
        long amountCents,
        string currency = RedsysSignature.EuroCurrency,
        string? merchant = null,
        string terminal = "1")
    {
        return await PostNoticeAsync(client, Notice(started, response, amountCents.ToString(), currency, merchant ?? Merchant, terminal));
    }

    private static Dictionary<string, string> Notice(
        StartedPayment started,
        string response,
        string amount,
        string currency,
        string merchant,
        string terminal)
    {
        var fields = new Dictionary<string, string>
        {
            ["Ds_Amount"] = amount,
            ["Ds_Currency"] = currency,
            ["Ds_Order"] = started.Operation,
            ["Ds_MerchantCode"] = merchant,
            ["Ds_Terminal"] = terminal,
            ["Ds_Response"] = response,
            ["Ds_TransactionType"] = "0"
        };
        var parameters = RedsysSignature.EncodeParameters(fields);
        return new Dictionary<string, string>
        {
            ["Ds_SignatureVersion"] = RedsysSignature.Version,
            ["Ds_MerchantParameters"] = parameters,
            ["Ds_Signature"] = RedsysSignature.Sign(parameters, started.Operation, ExampleKey)
        };
    }

    private static async Task<HttpResponseMessage> PostNoticeAsync(HttpClient client, Dictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync("/api/v1/checkout/redsys/notification", content);
    }

    private static async Task<CheckoutPaymentDto> ReadPaymentAsync(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<CheckoutPaymentDto>(JsonOptions()))!;
    }

    private static async Task<PaymentSnapshot> SnapshotAsync(WebApplicationFactory<Program> host, StartedPayment started)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).ThenInclude(payment => payment.Events).SingleAsync(item => item.Id == started.Payment.OrderId);
        var payment = order.Payments.Single();
        var reservation = await db.StockReservations.SingleAsync(item => item.OrderId == order.Id);
        var stock = await db.Stocks.Where(item => item.ProductId == started.ProductId).Select(item => item.Quantity).SingleAsync();
        return new PaymentSnapshot(
            order.Status,
            payment.Status,
            reservation.Status,
            stock,
            payment.Events.Count(item => item.ProcessedAt is not null),
            await db.Invoices.CountAsync(),
            payment.Events.Select(item => item.AttentionReason).Where(item => item is not null).Cast<string>().ToArray(),
            await db.OrderNotifications.CountAsync(item => item.OrderId == order.Id && item.Kind == OrderNotificationKind.PaidConfirmation));
    }

    private WebApplicationFactory<Program> RedsysHost(string environment = "Test")
    {
        return Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Stripe:Gateway", "Stub");
            builder.UseSetting("Redsys:Enabled", "true");
            builder.UseSetting("Redsys:Environment", environment);
            builder.UseSetting("Redsys:MerchantCode", Merchant);
            builder.UseSetting("Redsys:Terminal", "001");
            builder.UseSetting("Redsys:SignatureVersion", RedsysSignature.Version);
            builder.UseSetting("Redsys:SecretKey", ExampleKey);
            builder.UseSetting("Redsys:MerchantUrl", "https://localhost/api/v1/checkout/redsys/notification");
            builder.UseSetting("Redsys:UrlOk", "http://localhost:4200/checkout/exito");
            builder.UseSetting("Redsys:UrlKo", "http://localhost:4200/checkout/cancelado");
        });
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private sealed record StartedPayment(CheckoutPaymentDto Payment, Guid ProductId, string Operation);

    private sealed record PaymentSnapshot(
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        StockReservationStatus Reservation,
        int Stock,
        int Events,
        int Invoices,
        IReadOnlyList<string> Attention,
        int Confirmations);
}
