using System.Text.Json;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class OrderPersistenceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Persists_the_order_snapshot_when_the_catalog_price_changes()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ORD-WINE", ProductKind.Wine, vintage: "2024");
        var component = await harness.CreateProductAsync(categoryId, "ORD-COMP");
        var pack = await harness.CreateProductAsync(
            categoryId,
            "ORD-PACK",
            ProductKind.Pack,
            componentsJson: JsonSerializer.Serialize(new[] { new { productId = component.Id, quantity = 2 } }));
        var createdAt = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-2026-000001",
            "idem-order-1",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            "Portero",
            [
                new OrderLine(wine.Id, "Vino guardado", "ORD-WINE", ProductKind.Wine, 1, 1_890, 21m, null),
                new OrderLine(
                    pack.Id,
                    "Pack guardado",
                    "ORD-PACK",
                    ProductKind.Pack,
                    1,
                    5_400,
                    21m,
                    [new OrderLineComponent(component.Id, "Componente guardado", "ORD-COMP", 2)])
            ],
            createdAt);
        var payment = order.AddPayment(Guid.NewGuid(), createdAt);
        order.AssignCheckoutSession(payment.Id, "cs_test_persist");
        order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "evt_test_persist", "checkout.session.completed", createdAt);

        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Products" SET "Price" = 99.00 WHERE "Id" = {wine.Id}""");
        }

        await using var readScope = Fixture.Factory.Services.CreateAsyncScope();
        var stored = await readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders
            .Include(item => item.Items)
            .ThenInclude(item => item.Components)
            .Include(item => item.Payments)
            .ThenInclude(item => item.Events)
            .SingleAsync(item => item.Id == order.Id);
        var storedWine = stored.Items.Single(item => item.ProductId == wine.Id);
        var storedPack = stored.Items.Single(item => item.ProductId == pack.Id);

        Assert.Equal(1_890, storedWine.UnitPriceCents);
        Assert.Equal(1_562, storedWine.TaxableBaseCents);
        Assert.Equal(328, storedWine.VatCents);
        Assert.Equal("Vino guardado", storedWine.Name);
        Assert.Equal(2, Assert.Single(storedPack.Components).QuantityPerPack);
        Assert.Equal("Componente guardado", Assert.Single(storedPack.Components).Name);
        Assert.Equal(7_290, stored.ProductSubtotalCents);
        Assert.Equal(800, stored.ShippingCents);
        Assert.Equal(8_090, stored.TotalCents);
        Assert.Equal(createdAt.AddMinutes(30), stored.ReservationExpiresAt);
        Assert.Equal("cs_test_persist", Assert.Single(stored.Payments).StripeCheckoutSessionId);
        Assert.Equal("evt_test_persist", Assert.Single(Assert.Single(stored.Payments).Events).ExternalEventId);
    }

    [Fact]
    public async Task Rejects_a_duplicated_idempotency_key()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var wine = await harness.CreateProductAsync(categoryId, "ORD-IDEM", ProductKind.Wine, vintage: "2023");
        var first = Pending(wine.Id, "DS-2026-000002", "same-key");
        var second = Pending(wine.Id, "DS-2026-000003", "same-key");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Orders.Add(first);
        await db.SaveChangesAsync();
        db.Orders.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Rejects_an_order_whose_total_does_not_match_its_parts()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdAt = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Orders" (
                "Id", "Number", "Status", "CustomerName", "Email", "Phone", "AddressLine", "PostalCode",
                "City", "Province", "CountryCode", "DeliveryNotes", "ProductSubtotalCents", "ProductTaxableBaseCents",
                "ProductVatCents", "ShippingCents", "ShippingVatRate", "ShippingTaxableBaseCents", "ShippingVatCents",
                "TotalCents", "Currency", "IdempotencyKey", "CheckoutAccessTokenHash", "CreatedAt", "ReservationExpiresAt", "PaidAt", "CancelledAt", "ExpiredAt")
            VALUES (
                {Guid.NewGuid()}, {"DS-BAD-TOTAL"}, {"PendingPayment"}, {"Ana Rivas"}, {"ana@example.com"}, {"+34600111222"},
                {"Calle Mayor 1"}, {"37001"}, {"Salamanca"}, {"Salamanca"}, {"ES"}, {null},
                {1000}, {826}, {174}, {0}, {null}, {null}, {null},
                {1}, {"EUR"}, {"bad-total"}, {"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}, {createdAt}, {createdAt.AddMinutes(30)}, {null}, {null}, {null})
            """));

        Assert.Contains("CK_Orders_Amounts", exception.ToString(), StringComparison.Ordinal);
    }

    private static Order Pending(Guid productId, string number, string idempotencyKey)
    {
        return Order.CreatePending(
            Guid.NewGuid(),
            number,
            idempotencyKey,
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(productId, "Vino", "ORD-IDEM", ProductKind.Wine, 1, 1_890, 21m, null)],
            new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero));
    }
}
