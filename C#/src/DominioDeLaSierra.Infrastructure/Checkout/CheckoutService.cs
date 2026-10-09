using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Inventory;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public sealed class CheckoutService(ApplicationDbContext dbContext, IOrderStock orderStock) : ICheckout
{
    public async Task<CheckoutQuoteDto> QuoteAsync(CheckoutQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var postalCode = PeninsularSpain.RequirePostalCode(request.Destination?.PostalCode, request.Destination?.CountryCode);
        var lines = await BuildLinesAsync(NormalizeLines(request.Lines), cancellationToken);
        var draft = DraftOrder(lines, postalCode);
        await EnsureStockAsync(draft, cancellationToken);
        return MapQuote(draft);
    }

    public async Task<GuestOrderPlacement> PlaceGuestOrderAsync(
        string? idempotencyKey,
        PlaceGuestOrderRequest request,
        CancellationToken cancellationToken = default,
        TimeSpan? reservationDuration = null)
    {
        var key = RequireIdempotencyKey(idempotencyKey);
        var postalCode = PeninsularSpain.RequirePostalCode(request.Destination?.PostalCode, request.Destination?.CountryCode);
        var requested = NormalizeLines(request.Lines);
        var customer = NormalizeCustomer(request, postalCode);

        var existing = await FindOrderAsync(key, cancellationToken);
        if (existing is not null)
        {
            EnsureSameRequest(existing, customer, requested);
            return new GuestOrderPlacement(MapOrder(existing), Created: false);
        }

        var lines = await BuildLinesAsync(requested, cancellationToken);
        var accessToken = CheckoutAccessToken.Create();
        var accessHash = CheckoutAccessToken.Hash(accessToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var order = Order.CreatePending(
                Guid.NewGuid(),
                NextNumber(DateTimeOffset.UtcNow),
                key,
                customer.Name ?? string.Empty,
                customer.Email ?? string.Empty,
                customer.Phone ?? string.Empty,
                customer.AddressLine ?? string.Empty,
                customer.PostalCode,
                customer.City ?? string.Empty,
                customer.Province ?? string.Empty,
                customer.DeliveryNotes,
                lines,
                DateTimeOffset.UtcNow,
                accessHash,
                reservationDuration);

            try
            {
                await SaveAndReserveAsync(order, cancellationToken);
                return new GuestOrderPlacement(MapOrder(order, accessToken), Created: true);
            }
            catch (DbUpdateException exception) when (IsConstraint(exception, "IX_Orders_Number") && attempt < 2)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (IsConstraint(exception, "IX_Orders_IdempotencyKey"))
            {
                dbContext.ChangeTracker.Clear();
                var stored = await FindOrderAsync(key, cancellationToken);
                if (stored is null)
                {
                    throw new InvalidOperationException("No se ha podido recuperar el pedido.");
                }

                EnsureSameRequest(stored, customer, requested);
                return new GuestOrderPlacement(MapOrder(stored), Created: false);
            }
        }

        throw new InvalidOperationException("No se ha podido asignar un número de pedido.");
    }

    private async Task SaveAndReserveAsync(Order order, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await orderStock.ReserveAsync(order.Id, order.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OrderLine>> BuildLinesAsync(
        IReadOnlyList<CheckoutLineRequest> requested,
        CancellationToken cancellationToken)
    {
        var ids = requested.Select(line => line.ProductId).ToArray();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(cancellationToken);
        var byId = products.ToDictionary(product => product.Id);
        if (byId.Count != ids.Length)
        {
            throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
        }

        var packIds = products.Where(product => product.Kind == ProductKind.Pack).Select(product => product.Id).ToArray();
        var components = new List<ProductComponent>();
        if (packIds.Length > 0)
        {
            components = await dbContext.ProductComponents
                .AsNoTracking()
                .Where(component => packIds.Contains(component.PackProductId))
                .ToListAsync(cancellationToken);
        }
        var componentIds = components.Select(component => component.ComponentProductId).Distinct().ToArray();
        var componentProducts = componentIds.Length == 0
            ? new Dictionary<Guid, Product>()
            : await dbContext.Products
                .AsNoTracking()
                .Where(product => componentIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);

        var lines = new List<OrderLine>(requested.Count);
        foreach (var requestedLine in requested)
        {
            var product = byId[requestedLine.ProductId];
            if (!product.Active)
            {
                throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
            }

            if (product.Kind == ProductKind.Pack)
            {
                var parts = components
                    .Where(component => component.PackProductId == product.Id)
                    .OrderBy(component => component.ComponentProductId)
                    .ToArray();
                if (parts.Length == 0 || parts.Select(part => part.ComponentProductId).Distinct().Count() != parts.Length)
                {
                    throw new InvalidOperationException(CatalogComposition.InvalidCompositionMessage);
                }

                var snapshots = new List<OrderLineComponent>(parts.Length);
                foreach (var part in parts)
                {
                    if (!componentProducts.TryGetValue(part.ComponentProductId, out var component))
                    {
                        throw new InvalidOperationException(CatalogComposition.UnavailableComponentMessage);
                    }

                    if (!component.Active)
                    {
                        throw new InvalidOperationException(CatalogComposition.UnavailableComponentMessage);
                    }

                    if (!CatalogComposition.IsEligibleComponent(component.Kind) || component.Id == product.Id)
                    {
                        throw new InvalidOperationException(CatalogComposition.InvalidCompositionMessage);
                    }

                    snapshots.Add(new OrderLineComponent(component.Id, component.Name, component.Reference, part.Quantity));
                }

                lines.Add(new OrderLine(
                    product.Id,
                    product.Name,
                    product.Reference,
                    product.Kind,
                    requestedLine.Quantity,
                    OrderAmounts.PriceToCents(product.Price),
                    product.VatRate,
                    snapshots));
                continue;
            }

            if (product.Kind is not (ProductKind.Wine or ProductKind.Standard))
            {
                throw new InvalidOperationException(OrderStockRules.UnavailableProductMessage);
            }

            lines.Add(new OrderLine(
                product.Id,
                product.Name,
                product.Reference,
                product.Kind,
                requestedLine.Quantity,
                OrderAmounts.PriceToCents(product.Price),
                product.VatRate,
                null));
        }

        return lines;
    }

    private async Task EnsureStockAsync(Order draft, CancellationToken cancellationToken)
    {
        var demand = StockDemand.Collect(draft.Items);
        var warehouseId = await InventoryStock.FindActiveDefaultWarehouseIdAsync(dbContext, cancellationToken)
            ?? throw new InvalidOperationException(InventoryStock.MissingWarehouseMessage);
        var ids = demand.Select(item => item.ProductId).ToArray();
        var stocks = await dbContext.Stocks
            .AsNoTracking()
            .Where(stock => stock.WarehouseId == warehouseId && ids.Contains(stock.ProductId))
            .Select(stock => new { stock.ProductId, stock.Quantity })
            .ToListAsync(cancellationToken);
        var available = stocks.ToDictionary(stock => stock.ProductId, stock => stock.Quantity);
        foreach (var (productId, quantity) in demand)
        {
            if (!available.TryGetValue(productId, out var onHand) || onHand < quantity)
            {
                throw new InvalidOperationException(OrderStockRules.InsufficientStockMessage);
            }
        }
    }

    private async Task<Order?> FindOrderAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    private static Order DraftOrder(IReadOnlyList<OrderLine> lines, string postalCode)
    {
        return Order.CreatePending(
            Guid.NewGuid(),
            "DS-QUOTE",
            "quote-" + Guid.NewGuid().ToString("N"),
            "Presupuesto",
            "presupuesto@example.com",
            "600000000",
            "Presupuesto",
            postalCode,
            "Presupuesto",
            "Presupuesto",
            null,
            lines,
            DateTimeOffset.UtcNow);
    }

    private static CustomerSnapshot NormalizeCustomer(PlaceGuestOrderRequest request, string postalCode)
    {
        return new CustomerSnapshot(
            request.CustomerName,
            request.Email,
            request.Phone,
            request.AddressLine,
            postalCode,
            request.City,
            request.Province,
            string.IsNullOrWhiteSpace(request.DeliveryNotes) ? null : request.DeliveryNotes);
    }

    private static IReadOnlyList<CheckoutLineRequest> NormalizeLines(IReadOnlyList<CheckoutLineRequest>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            throw new ArgumentException(CheckoutLimits.EmptyOrderMessage);
        }

        if (requested.Count > CheckoutLimits.MaxLines)
        {
            throw new ArgumentException(CheckoutLimits.TooManyLinesMessage);
        }

        var seen = new HashSet<Guid>();
        foreach (var line in requested)
        {
            if (line.ProductId == Guid.Empty)
            {
                throw new ArgumentException(OrderStockRules.UnavailableProductMessage);
            }

            if (line.Quantity < 1)
            {
                throw new ArgumentException(CheckoutLimits.QuantityMessage);
            }

            if (line.Quantity > CheckoutLimits.MaxQuantityPerLine)
            {
                throw new ArgumentException(CheckoutLimits.TooMuchQuantityMessage);
            }

            if (!seen.Add(line.ProductId))
            {
                throw new ArgumentException(CheckoutLimits.RepeatedProductMessage);
            }
        }

        return requested;
    }

    private static void EnsureSameRequest(
        Order stored,
        CustomerSnapshot customer,
        IReadOnlyList<CheckoutLineRequest> lines)
    {
        var probe = Order.CreatePending(
            Guid.NewGuid(),
            "DS-QUOTE",
            "quote-" + Guid.NewGuid().ToString("N"),
            customer.Name ?? string.Empty,
            customer.Email ?? string.Empty,
            customer.Phone ?? string.Empty,
            customer.AddressLine ?? string.Empty,
            customer.PostalCode,
            customer.City ?? string.Empty,
            customer.Province ?? string.Empty,
            customer.DeliveryNotes,
            [new OrderLine(lines[0].ProductId, "Comparación", "CMP", ProductKind.Standard, lines[0].Quantity, 100, 21m, null)],
            stored.CreatedAt);

        var sameCustomer = stored.CustomerName == probe.CustomerName
            && stored.Email == probe.Email
            && stored.Phone == probe.Phone
            && stored.AddressLine == probe.AddressLine
            && stored.PostalCode == probe.PostalCode
            && stored.City == probe.City
            && stored.Province == probe.Province
            && stored.DeliveryNotes == probe.DeliveryNotes;
        var storedLines = stored.Items
            .OrderBy(item => item.ProductId)
            .Select(item => (item.ProductId, item.Quantity))
            .ToArray();
        var requestedLines = lines
            .OrderBy(line => line.ProductId)
            .Select(line => (line.ProductId, line.Quantity))
            .ToArray();
        if (!sameCustomer || !storedLines.SequenceEqual(requestedLines))
        {
            throw new InvalidOperationException(CheckoutLimits.IdempotencyConflictMessage);
        }
    }

    private static string RequireIdempotencyKey(string? idempotencyKey)
    {
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > Order.IdempotencyKeyMaxLength || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(CheckoutLimits.IdempotencyMessage);
        }

        return key;
    }

    private static string NextNumber(DateTimeOffset at)
    {
        return $"DS-{at:yyyyMMdd}-{Guid.NewGuid():N}"[..20];
    }

    private static bool IsConstraint(DbUpdateException exception, string constraintName)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static CheckoutQuoteDto MapQuote(Order order)
    {
        return new CheckoutQuoteDto(
            order.Items.Select(item => new CheckoutQuoteLineDto(
                item.ProductId,
                item.Name,
                item.Reference,
                item.Kind.ToString(),
                item.Quantity,
                item.UnitPriceCents,
                item.VatRate,
                item.LineTotalCents,
                item.TaxableBaseCents,
                item.VatCents)).ToArray(),
            order.ProductSubtotalCents,
            order.ProductTaxableBaseCents,
            order.ProductVatCents,
            order.ShippingCents,
            order.ShippingVatRate,
            order.TotalCents,
            order.Currency,
            order.ShippingVatRate is null);
    }

    private static GuestOrderDto MapOrder(Order order, string? checkoutAccessToken = null)
    {
        return new GuestOrderDto(
            order.Id,
            order.Number,
            order.ProductSubtotalCents,
            order.ProductTaxableBaseCents,
            order.ProductVatCents,
            order.ShippingCents,
            order.ShippingVatRate,
            order.TotalCents,
            order.Currency,
            order.ReservationExpiresAt,
            order.Status.ToString(),
            order.ShippingVatRate is null,
            checkoutAccessToken);
    }

    private sealed record CustomerSnapshot(
        string? Name,
        string? Email,
        string? Phone,
        string? AddressLine,
        string PostalCode,
        string? City,
        string? Province,
        string? DeliveryNotes);
}
