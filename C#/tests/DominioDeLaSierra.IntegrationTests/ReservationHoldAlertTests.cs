using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class ReservationHoldAlertTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Does_not_open_an_incident_one_minute_before_24_hours()
    {
        var held = await PlaceAsync("hold-23h59");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        var result = await ReviewAsync(Now);

        Assert.Equal(0, result.Opened);
        Assert.Empty(await IncidentsAsync(held.OrderId));
        await AssertUntouchedAsync(held);
    }

    [Fact]
    public async Task Opens_one_incident_exactly_at_24_hours_and_ignores_the_repeat()
    {
        var held = await PlaceAsync("hold-24h");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(24));

        var first = await ReviewAsync(Now);
        var second = await ReviewAsync(Now);
        var incidents = await IncidentsAsync(held.OrderId);

        Assert.Equal(1, first.Opened);
        Assert.Equal(0, second.Opened);
        Assert.Equal(1, second.Unchanged);
        var incident = Assert.Single(incidents);
        Assert.Equal("Held", incident.Severity);
        Assert.Equal("Holding", incident.Reconciliation);
        Assert.Equal(24 * 60 * 60, incident.AgeSeconds);
        Assert.Equal(Now, incident.OpenedAt);
        Assert.Equal(Now, incident.UpdatedAt);
        Assert.Equal(Now.AddHours(24), incident.NextReviewAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal(1, incident.AlertCount);
        await AssertUntouchedAsync(held);
    }

    [Fact]
    public async Task Opens_an_incident_one_minute_after_24_hours()
    {
        var held = await PlaceAsync("hold-24h01");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));

        var result = await ReviewAsync(Now);
        var incident = Assert.Single(await IncidentsAsync(held.OrderId));

        Assert.Equal(1, result.Opened);
        Assert.Equal("Held", incident.Severity);
        Assert.Equal((24 * 60 * 60) + 60, incident.AgeSeconds);
        await AssertUntouchedAsync(held);
    }

    [Fact]
    public async Task Keeps_the_same_incident_after_the_process_restarts()
    {
        var held = await PlaceAsync("hold-restart");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(25));
        var created = Assert.Single(await ReviewAndReadAsync(Now));

        var again = await ReviewAndReadAsync(Now.AddMinutes(5));

        var incident = Assert.Single(again);
        Assert.Equal(created.Id, incident.Id);
        Assert.Equal(created.OpenedAt, incident.OpenedAt);
        Assert.Equal(created.UpdatedAt, incident.UpdatedAt);
        Assert.Equal(1, incident.AlertCount);
        Assert.Null(incident.ResolvedAt);
        await AssertUntouchedAsync(held);
    }

    [Fact]
    public async Task Opens_a_critical_incident_immediately_when_a_confirmed_payment_cannot_settle()
    {
        var held = await PlaceAsync("hold-critical");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(1));
        await RecordUnsettledPaymentAsync(held.OrderId, Now);

        var result = await ReviewAsync(Now);
        var incident = Assert.Single(await IncidentsAsync(held.OrderId));

        Assert.Equal(1, result.Opened);
        Assert.Equal("Critical", incident.Severity);
        Assert.Equal("PaidUnsettled", incident.Reconciliation);
        Assert.Equal(PaymentProvider.PayPal, incident.Provider);
        Assert.Equal(60 * 60, incident.AgeSeconds);
        await AssertUntouchedAsync(held, PaymentStatus.Pending);
        Assert.Equal(0, await NotificationCountAsync(held.OrderId));
    }

    [Fact]
    public async Task Does_not_reopen_a_hold_after_an_administrative_review()
    {
        var held = await PlaceAsync("hold-admin");
        await AgeReservationAsync(held.OrderId, Now - TimeSpan.FromHours(30));
        await ReviewAsync(Now);
        Assert.True(await ResolveAsync());

        var again = await ReviewAsync(Now.AddDays(2));

        Assert.Equal(0, again.Opened);
        var incident = Assert.Single(await IncidentsAsync(held.OrderId));
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal("Administrative", incident.Resolution);
        await AssertUntouchedAsync(held);
    }

    [Fact]
    public void Leaves_the_sweep_disabled()
    {
        var settings = Fixture.Factory.Services.GetRequiredService<IOptions<ReservationSweepOptions>>().Value;

        Assert.False(settings.SweepEnabled);
        Assert.False(settings.HoldAlertEnabled);
        Assert.Equal(24, settings.HoldAlertHours);
    }

    private async Task<ReservationHoldAlertResult> ReviewAsync(DateTimeOffset now)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationHoldAlerts>();
        return await service.RunOnceAsync(now);
    }

    private async Task<bool> ResolveAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var orderId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders
            .Select(item => item.Id)
            .SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<IReservationHoldAlerts>().ResolveAdministrativelyAsync(orderId);
    }

    private async Task<IReadOnlyList<ReservationHoldIncident>> ReviewAndReadAsync(DateTimeOffset now)
    {
        await ReviewAsync(now);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ReservationHoldIncidents
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task<HeldOrder> PlaceAsync(string key)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, key, ProductKind.Wine, initialStock: 5, vintage: "2024");
        var client = Fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/orders")
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
        var order = JsonSerializer.Deserialize<GuestOrderDto>(await response.Content.ReadAsStringAsync(), JsonOptions())!;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new HeldOrder(order.OrderId, wine.Id);
    }

    private async Task AgeReservationAsync(Guid orderId, DateTimeOffset reservedAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "StockReservations" SET "ReservedAt" = {reservedAt} WHERE "OrderId" = {orderId}""");
    }

    private async Task RecordUnsettledPaymentAsync(Guid orderId, DateTimeOffset at)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == orderId);
        var payment = order.AddPayment(Guid.NewGuid(), at, PaymentProvider.PayPal);
        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "capture-local", "orders.capture", at);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, at, PayPalRules.StockMessage);
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<ReservationHoldIncident>> IncidentsAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ReservationHoldIncidents
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .ToListAsync();
    }

    private async Task AssertUntouchedAsync(HeldOrder held, PaymentStatus? paymentStatus = null)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == held.OrderId);
        var reservation = await db.StockReservations.SingleAsync(item => item.OrderId == held.OrderId);
        var stock = await db.Stocks.Where(item => item.ProductId == held.ProductId).Select(item => item.Quantity).SingleAsync();
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Null(order.CancelledAt);
        Assert.Null(order.ExpiredAt);
        Assert.Equal(StockReservationStatus.Reserved, reservation.Status);
        Assert.Equal(4, stock);
        if (paymentStatus is null)
        {
            Assert.Empty(order.Payments);
        }
        else
        {
            Assert.Equal(paymentStatus, Assert.Single(order.Payments).Status);
        }
    }

    private async Task<int> NotificationCountAsync(Guid orderId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OrderNotifications
            .CountAsync(item => item.OrderId == orderId);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private sealed record HeldOrder(Guid OrderId, Guid ProductId);
}
