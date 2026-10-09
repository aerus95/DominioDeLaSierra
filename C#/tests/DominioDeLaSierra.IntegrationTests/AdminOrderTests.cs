using System.Net;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class AdminOrderTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Lists_orders_newest_first_and_pages_on_the_server()
    {
        await TestCatalog.SeedAsync();
        var productId = await CreateProductAsync("ADM-PAGE");
        var oldest = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 21; index++)
            {
                db.Orders.Add(Pending(productId, $"DS-PAGE-{index:00}", $"page-{index}", "Cliente Lista", oldest.AddHours(index)));
            }

            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);
        var first = await client.GetStringAsync("/admin/orders");
        var second = await client.GetStringAsync("/admin/orders?listPage=2");
        var panel = await client.GetStringAsync("/admin");

        Assert.Contains("DS-PAGE-20", first, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-PAGE-00", first, StringComparison.Ordinal);
        Assert.Contains("DS-PAGE-00", second, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-PAGE-20", second, StringComparison.Ordinal);
        Assert.Contains("Página 2 de 2", second, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/orders\"", panel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Combines_number_customer_status_date_and_payment_filters()
    {
        await TestCatalog.SeedAsync();
        var productId = await CreateProductAsync("ADM-FILTER");
        var onDay = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
        var nextLocalDay = new DateTimeOffset(2026, 10, 8, 22, 30, 0, TimeSpan.Zero);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(Paid(productId, "DS-MATCH-100", "match", "Lucía Blanco", onDay, FulfillmentStatus.Preparing));
            db.Orders.Add(Paid(productId, "DS-OTHER-NAME", "other-name", "Ana Rivas", onDay, FulfillmentStatus.Preparing));
            db.Orders.Add(Pending(productId, "DS-OTHER-STATUS", "other-status", "Lucía Blanco", onDay));
            db.Orders.Add(Paid(productId, "DS-OTHER-DAY", "other-day", "Lucía Blanco", nextLocalDay, FulfillmentStatus.Preparing));
            db.Orders.Add(Paid(productId, "DS-OTHER-PREP", "other-prep", "Lucía Blanco", onDay, FulfillmentStatus.Unfulfilled));
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);
        var filtered = await client.GetAsync(
            "/admin/orders?number=DS-MATCH&customer=Luc%C3%ADa&status=Paid&fulfillment=Preparing&payment=Succeeded&from=2026-10-08&to=2026-10-08");
        var html = await filtered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);

        Assert.Contains("DS-MATCH-100", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-OTHER-NAME", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-OTHER-STATUS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-OTHER-DAY", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DS-OTHER-PREP", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_the_pack_snapshot_payment_incident_and_pending_shipping_vat()
    {
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var component = await harness.CreateProductAsync(categoryId, "ADM-COMP");
        var pack = await harness.CreateProductAsync(categoryId, "ADM-PACK", ProductKind.Pack, componentsJson:
            System.Text.Json.JsonSerializer.Serialize(new[] { new { productId = component.Id, quantity = 2 } }));
        var createdAt = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-PACK-1",
            "pack-detail",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            "Portero",
            [
                new OrderLine(
                    pack.Id,
                    "Pack guardado",
                    "ADM-PACK",
                    ProductKind.Pack,
                    1,
                    5_400,
                    21m,
                    [new OrderLineComponent(component.Id, "Componente guardado", "ADM-COMP", 2)])
            ],
            createdAt);
        var payment = order.AddPayment(Guid.NewGuid(), createdAt);
        order.AssignHostedCheckout(
            payment.Id,
            "cs_test_admin_detail",
            "https://checkout.stripe.com/c/pay/secret-session",
            createdAt.AddMinutes(30));
        order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "evt_test_admin_detail", "checkout.session.completed", createdAt);
        order.MarkPaymentEventProcessed(payment.Id, "evt_test_admin_detail", createdAt, "El importe no coincide.");
        order.MarkPaid(payment.Id, createdAt.AddMinutes(2));
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(order);
            var storedComponent = await db.Products.SingleAsync(item => item.Id == component.Id);
            storedComponent.UpdateCatalog(
                storedComponent.Reference,
                "Nombre vivo del catálogo",
                storedComponent.Slug,
                "Descripción de prueba",
                categoryId,
                18.90m,
                21m,
                true,
                createdAt);
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ViewerUsername);
        var html = await client.GetStringAsync($"/admin/orders/{order.Id}");

        Assert.Contains("Componente guardado", html, StringComparison.Ordinal);
        Assert.Contains("2 por pack", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nombre vivo del catálogo", html, StringComparison.Ordinal);
        Assert.Contains("Pendiente", html, StringComparison.Ordinal);
        Assert.Contains("El importe no coincide.", html, StringComparison.Ordinal);
        Assert.Contains("cs_test_admin_detail", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-session", html, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout.stripe.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain(order.CheckoutAccessTokenHash, html, StringComparison.Ordinal);
        Assert.Contains("Tu rol permite consultar pedidos", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Marcar como", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrator_advances_a_paid_order_without_changing_stock_or_payment()
    {
        await TestCatalog.SeedAsync();
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var product = await harness.CreateProductAsync(categoryId, "ADM-STOCK", ProductKind.Wine, 4, vintage: "2024");
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var order = Pending(product.Id, "DS-SHIP-1", "ship-1", "Ana Rivas", createdAt, "ADM-STOCK");
        var paymentId = order.Payments.Single().Id;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var stock = scope.ServiceProvider.GetRequiredService<IOrderStock>();
            await stock.ReserveAsync(order.Id, createdAt.AddMinutes(1));
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Orders.Include(item => item.Payments).SingleAsync(item => item.Id == order.Id);
            stored.MarkPaid(paymentId, createdAt.AddMinutes(2));
            await db.SaveChangesAsync();
        }

        int stockBefore;
        int movementsBefore;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            stockBefore = await db.Stocks.Where(item => item.ProductId == product.Id).Select(item => item.Quantity).SingleAsync();
            movementsBefore = await db.StockMovements.CountAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);
        var preparing = await PostAdvanceAsync(client, order.Id, "Preparing", null, null);
        var prepared = await PostAdvanceAsync(client, order.Id, "Prepared", "Seur", null);
        var shipped = await PostAdvanceAsync(client, order.Id, "Shipped", null, "SEG-100");
        var duplicate = await PostAdvanceAsync(client, order.Id, "Shipped", "Seur", "SEG-100");
        var paidToken = Regex.Match(shipped, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var paidAttempt = await client.PostAsync($"/admin/orders/{order.Id}?handler=MarkPaid", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = paidToken
        }));

        await using var check = Fixture.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedOrder = await database.Orders.Include(item => item.FulfillmentTransitions).SingleAsync(item => item.Id == order.Id);
        var reservation = await database.StockReservations.SingleAsync(item => item.OrderId == order.Id);
        var stockAfter = await database.Stocks.Where(item => item.ProductId == product.Id).Select(item => item.Quantity).SingleAsync();

        Assert.Contains("El estado de preparación se ha actualizado.", preparing, StringComparison.Ordinal);
        Assert.Contains("El estado de preparación se ha actualizado.", prepared, StringComparison.Ordinal);
        Assert.Contains("E2E Admin", shipped, StringComparison.Ordinal);
        Assert.Contains("SEG-100", shipped, StringComparison.Ordinal);
        Assert.Contains("El pedido ya está en ese estado de preparación.", duplicate, StringComparison.Ordinal);
        var paidBody = WebUtility.HtmlDecode(await paidAttempt.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Marcar como pagado", paidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, paidAttempt.StatusCode);
        Assert.Equal(OrderStatus.Paid, storedOrder.Status);
        Assert.Equal(FulfillmentStatus.Shipped, storedOrder.FulfillmentStatus);
        Assert.Equal(3, storedOrder.FulfillmentTransitions.Count);
        Assert.Equal(stockBefore, stockAfter);
        Assert.Equal(movementsBefore, await database.StockMovements.CountAsync());
        Assert.Equal(StockReservationStatus.Reserved, reservation.Status);
    }

    [Fact]
    public async Task Pending_payment_cannot_be_prepared_and_a_paid_order_cannot_skip_a_step()
    {
        await TestCatalog.SeedAsync();
        var productId = await CreateProductAsync("ADM-BLOCK");
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var pending = Pending(productId, "DS-PENDING-1", "pending-1", "Ana Rivas", createdAt);
        var paid = Paid(productId, "DS-SKIP-1", "skip-1", "Ana Rivas", createdAt, FulfillmentStatus.Unfulfilled);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.AddRange(pending, paid);
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ManagerUsername);
        var blocked = await PostAdvanceAsync(client, pending.Id, "Preparing", null, null);
        var skipped = await PostAdvanceAsync(client, paid.Id, "Shipped", null, null);
        var allowed = await PostAdvanceAsync(client, paid.Id, "Preparing", null, null);

        await using var check = Fixture.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pendingOrder = await database.Orders.SingleAsync(item => item.Id == pending.Id);
        var paidOrder = await database.Orders.Include(item => item.FulfillmentTransitions).SingleAsync(item => item.Id == paid.Id);

        Assert.Contains("Solo se puede preparar un pedido pagado.", blocked, StringComparison.Ordinal);
        Assert.Contains("El pedido no puede pasar a ese estado de preparación.", skipped, StringComparison.Ordinal);
        Assert.Contains("E2E Manager", allowed, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.PendingPayment, pendingOrder.Status);
        Assert.Equal(FulfillmentStatus.Unfulfilled, pendingOrder.FulfillmentStatus);
        Assert.Equal(OrderStatus.Paid, paidOrder.Status);
        Assert.Equal(FulfillmentStatus.Preparing, paidOrder.FulfillmentStatus);
        Assert.Single(paidOrder.FulfillmentTransitions);
    }

    [Fact]
    public async Task Viewer_cannot_change_fulfillment()
    {
        await TestCatalog.SeedAsync();
        var productId = await CreateProductAsync("ADM-VIEW");
        var order = Paid(productId, "DS-VIEW-1", "view-1", "Ana Rivas", DateTimeOffset.UtcNow.AddMinutes(-5), FulfillmentStatus.Unfulfilled);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ViewerUsername);
        var page = await client.GetAsync($"/admin/orders/{order.Id}");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync($"/admin/orders/{order.Id}?handler=Advance", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["target"] = "Preparing",
            ["__RequestVerificationToken"] = token
        }));

        await using var check = Fixture.Factory.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await database.Orders.Include(item => item.FulfillmentTransitions).SingleAsync(item => item.Id == order.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("No tienes permiso para gestionar pedidos.", await response.Content.ReadAsStringAsync());
        Assert.Equal(FulfillmentStatus.Unfulfilled, stored.FulfillmentStatus);
        Assert.Empty(stored.FulfillmentTransitions);
        Assert.Equal(OrderStatus.Paid, stored.Status);
    }

    private async Task<Guid> CreateProductAsync(string reference)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var product = await harness.CreateProductAsync(categoryId, reference, ProductKind.Wine, vintage: "2024");
        return product.Id;
    }

    private static Order Pending(Guid productId, string number, string idempotencyKey, string customer, DateTimeOffset createdAt, string reference = "ADM-WINE")
    {
        var order = Order.CreatePending(
            Guid.NewGuid(),
            number,
            idempotencyKey,
            customer,
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(productId, "Vino", reference, ProductKind.Wine, 1, 1_890, 21m, null)],
            createdAt);
        order.AddPayment(Guid.NewGuid(), createdAt);
        return order;
    }

    private static Order Paid(
        Guid productId,
        string number,
        string idempotencyKey,
        string customer,
        DateTimeOffset createdAt,
        FulfillmentStatus fulfillment,
        string reference = "ADM-WINE")
    {
        var order = Pending(productId, number, idempotencyKey, customer, createdAt, reference);
        var paymentId = order.Payments.Single().Id;
        order.MarkPaid(paymentId, createdAt.AddMinutes(1));
        if (fulfillment != FulfillmentStatus.Unfulfilled)
        {
            var at = createdAt.AddMinutes(2);
            foreach (var step in new[] { FulfillmentStatus.Preparing, FulfillmentStatus.Prepared, FulfillmentStatus.Shipped })
            {
                order.AdvanceFulfillment(step, at, Guid.NewGuid(), null, null);
                if (step == fulfillment)
                {
                    break;
                }

                at = at.AddMinutes(1);
            }
        }

        return order;
    }

    private HttpClient CreateClient()
    {
        var handler = new CookieHandler
        {
            InnerHandler = Fixture.Factory.Server.CreateHandler()
        };
        return new HttpClient(handler)
        {
            BaseAddress = Fixture.Factory.Server.BaseAddress
        };
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

    private static async Task<string> PostAdvanceAsync(HttpClient client, Guid orderId, string target, string? carrier, string? tracking)
    {
        var page = await client.GetAsync($"/admin/orders/{orderId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["target"] = target,
            ["__RequestVerificationToken"] = token
        };
        if (carrier is not null)
        {
            fields["carrier"] = carrier;
        }

        if (tracking is not null)
        {
            fields["trackingNumber"] = tracking;
        }

        var response = await client.PostAsync($"/admin/orders/{orderId}?handler=Advance", new FormUrlEncodedContent(fields));
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
        {
            var location = response.Headers.Location;
            var path = location is null
                ? $"/admin/orders/{orderId}"
                : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            return WebUtility.HtmlDecode(await client.GetStringAsync(path));
        }

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
