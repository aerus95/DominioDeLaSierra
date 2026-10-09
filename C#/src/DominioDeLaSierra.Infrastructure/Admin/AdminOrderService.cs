using System.Data;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Admin;

public sealed class AdminOrderService(ApplicationDbContext dbContext) : IAdminOrders
{
    public async Task<AdminOrderListPage> ListAsync(AdminOrderListQuery query, CancellationToken cancellationToken = default)
    {
        if (query.InvertedDates)
        {
            return new AdminOrderListPage([], query.Page, AdminOrderListQuery.PageSize, 0);
        }

        var orders = dbContext.Orders.AsNoTracking();
        if (query.Number is { } number)
        {
            var pattern = ContainsPattern(number);
            orders = orders.Where(order => EF.Functions.ILike(order.Number, pattern, "\\"));
        }

        if (query.Customer is { } customer)
        {
            var pattern = ContainsPattern(customer);
            orders = orders.Where(order => EF.Functions.ILike(order.CustomerName, pattern, "\\"));
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (query.Fulfillment is { } fulfillment)
        {
            orders = orders.Where(order => order.FulfillmentStatus == fulfillment);
        }

        if (query.Payment is { } payment)
        {
            orders = orders.Where(order => order.Payments
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Select(item => (PaymentStatus?)item.Status)
                .FirstOrDefault() == payment);
        }

        if (query.From is { } from)
        {
            var start = AdminOrderClock.StartOfDay(from);
            orders = orders.Where(order => order.CreatedAt >= start);
        }

        if (query.To is { } to)
        {
            var end = AdminOrderClock.StartOfDay(to.AddDays(1));
            orders = orders.Where(order => order.CreatedAt < end);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var pageCount = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)AdminOrderListQuery.PageSize);
        var page = pageCount == 0 ? 1 : Math.Min(query.Page, pageCount);
        var items = await orders
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * AdminOrderListQuery.PageSize)
            .Take(AdminOrderListQuery.PageSize)
            .Select(order => new AdminOrderListItem(
                order.Id,
                order.Number,
                order.CreatedAt,
                order.CustomerName,
                order.TotalCents,
                order.Currency,
                order.Status,
                order.FulfillmentStatus,
                order.Payments
                    .OrderByDescending(payment => payment.CreatedAt)
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => (PaymentStatus?)payment.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new AdminOrderListPage(items, page, AdminOrderListQuery.PageSize, totalCount);
    }

    public async Task<AdminOrderDetail?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .ThenInclude(item => item.Components)
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .Include(item => item.FulfillmentTransitions)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var reservation = await dbContext.StockReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var actorIds = order.FulfillmentTransitions.Select(item => item.ActorUserId).Distinct().ToArray();
        var actors = actorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.AdminUsers
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);

        return new AdminOrderDetail(
            order.Id,
            order.Number,
            order.CreatedAt,
            order.ReservationExpiresAt,
            order.Status,
            order.FulfillmentStatus,
            order.Carrier,
            order.TrackingNumber,
            order.FulfillmentUpdatedAt,
            order.CustomerName,
            order.Email,
            order.Phone,
            order.AddressLine,
            order.PostalCode,
            order.City,
            order.Province,
            order.CountryCode,
            order.DeliveryNotes,
            order.ProductSubtotalCents,
            order.ProductVatCents,
            order.ShippingCents,
            order.ShippingVatRate,
            order.ShippingVatCents,
            order.TotalCents,
            order.Currency,
            order.Items
                .OrderBy(item => item.Name)
                .Select(item => new AdminOrderLine(
                    item.Name,
                    item.Reference,
                    item.Kind,
                    item.Quantity,
                    item.UnitPriceCents,
                    item.LineTotalCents,
                    item.Components
                        .OrderBy(component => component.Name)
                        .Select(component => new AdminOrderComponent(
                            component.Name,
                            component.Reference,
                            component.QuantityPerPack))
                        .ToArray()))
                .ToArray(),
            order.Payments
                .OrderByDescending(payment => payment.CreatedAt)
                .Select(payment => new AdminOrderPayment(
                    payment.Status,
                    payment.AmountCents,
                    payment.Currency,
                    payment.StripeCheckoutSessionId,
                    payment.StripePaymentIntentId,
                    payment.CreatedAt,
                    payment.SucceededAt,
                    payment.Events
                        .Where(paymentEvent => paymentEvent.AttentionReason != null)
                        .OrderBy(paymentEvent => paymentEvent.ReceivedAt)
                        .Select(paymentEvent => new AdminPaymentIncident(
                            paymentEvent.EventType,
                            paymentEvent.ReceivedAt,
                            paymentEvent.AttentionReason!))
                        .ToArray()))
                .ToArray(),
            reservation is null
                ? null
                : new AdminOrderReservation(
                    reservation.Status,
                    reservation.ReservedAt,
                    reservation.ConfirmationStartedAt,
                    reservation.ConfirmedAt,
                    reservation.ReleasedAt),
            order.FulfillmentTransitions
                .OrderBy(change => change.OccurredAt)
                .Select(change => new AdminFulfillmentChange(
                    change.FromStatus,
                    change.ToStatus,
                    change.OccurredAt,
                    actors.TryGetValue(change.ActorUserId, out var name) ? name : "Usuario no disponible",
                    change.Carrier,
                    change.TrackingNumber))
                .ToArray());
    }

    public async Task AdvanceFulfillmentAsync(AdvanceFulfillment command, CancellationToken cancellationToken = default)
    {
        if (command.Target is FulfillmentStatus.Unfulfilled || !Enum.IsDefined(command.Target))
        {
            throw new ArgumentException("El estado de preparación no es válido.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(command.OrderId, cancellationToken))
        {
            throw new InvalidOperationException("El pedido no existe.");
        }

        var actorIsActive = await dbContext.AdminUsers
            .AsNoTracking()
            .AnyAsync(user => user.Id == command.ActorUserId && user.Active, cancellationToken);
        if (!actorIsActive)
        {
            throw new InvalidOperationException("No se ha podido identificar al usuario.");
        }

        var order = await dbContext.Orders
            .Include(item => item.FulfillmentTransitions)
            .SingleAsync(item => item.Id == command.OrderId, cancellationToken);
        order.AdvanceFulfillment(
            command.Target,
            DateTimeOffset.UtcNow,
            command.ActorUserId,
            command.Carrier,
            command.TrackingNumber);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> LockOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("El pedido no existe.");
        command.CommandText = """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = orderId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static string ContainsPattern(string value)
    {
        return "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }
}
