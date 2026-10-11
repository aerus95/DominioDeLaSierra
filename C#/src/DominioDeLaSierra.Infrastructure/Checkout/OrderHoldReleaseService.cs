using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Caduca un pedido indicado y devuelve su reserva. Lee solo la sesión guardada
/// y no recorre otros pedidos.
/// </summary>
public sealed class OrderHoldReleaseService(
    ApplicationDbContext dbContext,
    IStripeCheckoutGateway gateway,
    IOrderStock orderStock,
    ILogger<OrderHoldReleaseService> logger) : IOrderHoldRelease
{
    public async Task<OrderHoldReleaseResult> ReleaseAsync(
        Guid orderId,
        string? orderNumber,
        CancellationToken cancellationToken = default)
    {
        var number = orderNumber?.Trim() ?? string.Empty;
        if (orderId == Guid.Empty || number.Length == 0)
        {
            return Refused(OrderHoldReleaseRules.SessionMessage);
        }

        var preview = await LoadAsync(orderId, cancellationToken);
        if (preview is null)
        {
            return Refused("El pedido no existe.");
        }

        if (!string.Equals(preview.Number, number, StringComparison.Ordinal))
        {
            return Refused(StripeWebhookRules.ReferenceMessage);
        }

        var payment = SelectPayment(preview);
        if (payment?.StripeCheckoutSessionId is null)
        {
            return Refused(OrderHoldReleaseRules.SessionMessage);
        }

        var read = await gateway.ReadStoredSessionAsync(payment.StripeCheckoutSessionId, cancellationToken);
        if (read.Availability != StoredSessionAvailability.Retrieved || read.Session is null)
        {
            return Refused(read.Availability == StoredSessionAvailability.Missing
                ? OrderHoldReleaseRules.SessionMessage
                : OrderHoldReleaseRules.UnavailableMessage);
        }

        var now = DateTimeOffset.UtcNow;
        var reservation = await dbContext.StockReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var verdict = OrderHoldReleaseRules.Decide(Context(preview, payment, reservation, read.Session, now));
        if (verdict.Decision is OrderHoldReleaseDecision.Refuse or OrderHoldReleaseDecision.AlreadySettled)
        {
            return Map(verdict);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Refused("El pedido no existe.");
        }

        dbContext.ChangeTracker.Clear();
        var current = await LoadAsync(orderId, cancellationToken);
        var currentPayment = current is null ? null : SelectPayment(current);
        var currentReservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (current is null
            || currentPayment?.StripeCheckoutSessionId is null
            || !string.Equals(current.Number, number, StringComparison.Ordinal)
            || !string.Equals(currentPayment.StripeCheckoutSessionId, payment.StripeCheckoutSessionId, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Refused(OrderHoldReleaseRules.SessionMessage);
        }

        now = DateTimeOffset.UtcNow;
        verdict = OrderHoldReleaseRules.Decide(Context(current, currentPayment, currentReservation, read.Session, now));
        if (verdict.Decision is OrderHoldReleaseDecision.Refuse or OrderHoldReleaseDecision.AlreadySettled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(verdict);
        }

        if (verdict.Decision == OrderHoldReleaseDecision.Release)
        {
            await orderStock.ReleaseAsync(orderId, now, cancellationToken);
        }

        current.Expire(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Pedido {OrderNumber} caducado y reserva liberada.", current.Number);
        return new OrderHoldReleaseResult(OrderHoldReleaseResultKind.Released, null);
    }

    private async Task<Order?> LoadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Include(order => order.Payments)
            .Include(order => order.Items)
            .ThenInclude(item => item.Components)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    private static Payment? SelectPayment(Order order)
    {
        var withSession = order.Payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
            .ToList();
        return withSession.Count == 1 ? withSession[0] : null;
    }

    private static OrderHoldReleaseContext Context(
        Order order,
        Payment payment,
        StockReservation? reservation,
        ObservedCheckoutSession session,
        DateTimeOffset now)
    {
        return new OrderHoldReleaseContext(
            order.Id,
            payment.StripeCheckoutSessionId,
            order.Status,
            payment.Status,
            order.TotalCents,
            payment.AmountCents,
            reservation?.Status,
            order.ReservationExpiresAt,
            now,
            session);
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
            ?? throw new InvalidOperationException("El pedido no está disponible.");
        command.CommandText = """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = orderId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static OrderHoldReleaseResult Map(OrderHoldReleaseVerdict verdict)
    {
        return verdict.Decision switch
        {
            OrderHoldReleaseDecision.AlreadySettled => new OrderHoldReleaseResult(
                OrderHoldReleaseResultKind.AlreadySettled,
                null),
            OrderHoldReleaseDecision.Release or OrderHoldReleaseDecision.FinishExpire => new OrderHoldReleaseResult(
                OrderHoldReleaseResultKind.Released,
                null),
            _ => Refused(verdict.Reason ?? StripeWebhookRules.ClosedOrderMessage)
        };
    }

    private static OrderHoldReleaseResult Refused(string reason)
    {
        return new OrderHoldReleaseResult(OrderHoldReleaseResultKind.Refused, reason);
    }
}
