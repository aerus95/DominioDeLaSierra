using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class PaymentProviderTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Lists_only_stripe_and_rejects_other_providers_before_reserving_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAY-LIST", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();

        var listed = await client.GetFromJsonAsync<List<PaymentProviderOfferDto>>("/api/v1/checkout/payment-providers");
        var offer = Assert.Single(listed!);
        Assert.Equal(nameof(PaymentProvider.Stripe), offer.Code);

        var redsys = await PostPaymentAsync(client, wine.Id, "Redsys");
        var paypal = await PostPaymentAsync(client, wine.Id, "PayPal");
        var unknown = await PostPaymentAsync(client, wine.Id, "Bizum");

        Assert.Equal(HttpStatusCode.Conflict, redsys.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paypal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains(PaymentProviderRules.UnavailableMessage, await redsys.Content.ReadAsStringAsync());
        Assert.Contains(PaymentProviderRules.UnknownMessage, await unknown.Content.ReadAsStringAsync());

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Equal(5, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Rejects_a_missing_or_blank_provider_without_reserving_stock()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAY-REQUIRED", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();

        var missing = await PostPaymentAsync(client, wine.Id, null);
        var blank = await PostPaymentAsync(client, wine.Id, " ");
        var first = PostPaymentAsync(client, wine.Id, null);
        var second = PostPaymentAsync(client, wine.Id, "");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Contains(PaymentProviderRules.RequiredMessage, await missing.Content.ReadAsStringAsync());
        var repeated = await Task.WhenAll(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, repeated[0].StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repeated[1].StatusCode);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Empty(await db.StockReservations.ToListAsync());
        Assert.Equal(5, await db.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task A_stripe_payment_stores_the_provider_and_the_persisted_total()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAY-STRIPE", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        var response = await PostPaymentAsync(client, wine.Id, "Stripe");
        var payment = JsonSerializer.Deserialize<CheckoutPaymentDto>(await response.Content.ReadAsStringAsync(), JsonOptions())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == payment.OrderId);
        var stored = Assert.Single(order.Payments);
        Assert.Equal(PaymentProvider.Stripe, stored.Provider);
        Assert.Equal(order.TotalCents, stored.AmountCents);
        Assert.Equal(payment.TotalCents, stored.AmountCents);
        Assert.Equal("EUR", stored.Currency);
        Assert.Equal(stored.StripeCheckoutSessionId, stored.ExternalOperationId);
        Assert.StartsWith("cs_test_", stored.StripeCheckoutSessionId, StringComparison.Ordinal);
        Assert.Null(stored.RedsysOrderNumber);
    }

    [Fact]
    public async Task Rejects_a_repeated_external_reference()
    {
        await AssertDuplicateAsync("redsys-shared", "100000000011", "redsys-shared", "100000000012", "IX_Payments_Provider_ExternalOperationId");
    }

    [Fact]
    public async Task Rejects_a_repeated_redsys_number()
    {
        await AssertDuplicateAsync("redsys-a", "100000000021", "redsys-b", "100000000021", "IX_Payments_RedsysOrderNumber");
    }

    [Fact]
    public async Task Allocates_distinct_numeric_redsys_operation_numbers()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IRedsysOperationNumbers>();
        var first = await numbers.NextAsync();
        var second = await numbers.NextAsync();

        Assert.Matches("^[0-9]{12}$", first);
        Assert.Matches("^[0-9]{12}$", second);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("DS-", first, StringComparison.Ordinal);
        Assert.DoesNotContain("F2026", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_release_an_expired_redsys_hold_without_provider_evidence()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAY-HOLD", ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
        {
            Content = JsonContent.Create(Body(wine.Id, null))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "pay-hold-" + Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        var placed = JsonSerializer.Deserialize<GuestOrderDto>(await response.Content.ReadAsStringAsync(), JsonOptions())!;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdAt = DateTimeOffset.UtcNow;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.SingleAsync(item => item.Id == placed.OrderId);
            AssignRedsys(order, createdAt, "redsys-hold-1", "100000000031");
            await db.SaveChangesAsync();
            var expires = createdAt.AddMinutes(-5);
            var orderCreated = expires.AddMinutes(-40);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Orders" SET "CreatedAt" = {orderCreated}, "ReservationExpiresAt" = {expires} WHERE "Id" = {placed.OrderId}""");
        }

        await using var sweepScope = Fixture.Factory.Services.CreateAsyncScope();
        var sweep = await sweepScope.ServiceProvider.GetRequiredService<IReservationSweep>().RunOnceAsync();

        Assert.Equal(1, sweep.Failed);
        Assert.Equal(0, sweep.Released);
        Assert.Equal(0, sweep.Confirmed);
        await using var readScope = Fixture.Factory.Services.CreateAsyncScope();
        var read = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(OrderStatus.PendingPayment, await read.Orders.Where(item => item.Id == placed.OrderId).Select(item => item.Status).SingleAsync());
        Assert.Equal(StockReservationStatus.Reserved, await read.StockReservations.Where(item => item.OrderId == placed.OrderId).Select(item => item.Status).SingleAsync());
        Assert.Equal(PaymentStatus.Pending, await read.Payments.Where(item => item.OrderId == placed.OrderId).Select(item => item.Status).SingleAsync());
        Assert.Equal(4, await read.Stocks.Where(item => item.ProductId == wine.Id).Select(item => item.Quantity).SingleAsync());
    }

    [Fact]
    public async Task Does_not_invoice_a_paid_order_from_an_unsupported_provider()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "PAY-INV", ProductKind.Wine, vintage: "2024");
        var createdAt = DateTimeOffset.UtcNow;
        Guid orderId;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = Order.CreatePending(
                Guid.NewGuid(),
                "DS-20261013-payinv",
                "pay-inv-" + Guid.NewGuid().ToString("N"),
                "Ana Rivas",
                "ana@example.com",
                "+34600111222",
                "Calle Mayor 1",
                "37001",
                "Salamanca",
                "Salamanca",
                null,
                [new OrderLine(wine.Id, "Dominium", "PAY-INV", ProductKind.Wine, 1, 1_890, 21m, null)],
                createdAt);
            AssignRedsys(order, createdAt, "redsys-invoice-1", "100000000041");
            order.MarkPaid(order.Payments.Single().Id, createdAt.AddMinutes(1));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var readScope = Fixture.Factory.Services.CreateAsyncScope();
        var panel = await readScope.ServiceProvider.GetRequiredService<IInvoiceIssuance>().DescribeAsync(orderId);
        var dbRead = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.NotNull(panel);
        Assert.False(panel.CanIssue);
        Assert.False(panel.Fictional);
        Assert.Equal(InvoiceIssuanceRules.UnspecifiedProviderMessage, panel.Notice);
        Assert.Equal(0, await dbRead.Invoices.CountAsync());
    }

    private async Task<HttpResponseMessage> PostPaymentAsync(HttpClient client, Guid productId, string? provider)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/payments")
        {
            Content = JsonContent.Create(Body(productId, provider))
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "pay-" + Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private async Task AssertDuplicateAsync(
        string firstOperation,
        string firstNumber,
        string secondOperation,
        string secondNumber,
        string constraint)
    {
        var first = await PendingOrderAsync(constraint + "-1");
        var second = await PendingOrderAsync(constraint + "-2");
        var createdAt = DateTimeOffset.UtcNow;
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orders = await db.Orders.Where(item => item.Id == first || item.Id == second).ToListAsync();
        AssignRedsys(orders.Single(item => item.Id == first), createdAt, firstOperation, firstNumber);
        AssignRedsys(orders.Single(item => item.Id == second), createdAt, secondOperation, secondNumber);
        var duplicated = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(constraint, PostgresConstraint(duplicated));
    }

    private async Task<Guid> PendingOrderAsync(string reference)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, vintage: "2024");
        var createdAt = DateTimeOffset.UtcNow;
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-" + Guid.NewGuid().ToString("N")[..12],
            reference + "-" + Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(wine.Id, "Dominium", reference, ProductKind.Wine, 1, 6_000, 21m, null)],
            createdAt);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static void AssignRedsys(Order order, DateTimeOffset createdAt, string operationId, string number)
    {
        var payment = order.AddPayment(Guid.NewGuid(), createdAt, PaymentProvider.Redsys);
        order.AssignExternalCheckout(
            payment.Id,
            operationId,
            "https://sis-t.redsys.es/sis/realizarPago",
            createdAt.AddMinutes(12),
            number);
    }

    private static string PostgresConstraint(DbUpdateException exception)
    {
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        return postgres.ConstraintName ?? string.Empty;
    }

    private static PlaceGuestOrderRequest Body(Guid productId, string? provider)
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
            null,
            provider);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }
}
