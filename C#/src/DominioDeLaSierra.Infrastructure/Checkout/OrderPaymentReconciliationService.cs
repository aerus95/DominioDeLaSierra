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
/// Confirma un pedido indicado, leyendo solo su sesión guardada con la clave de pruebas.
/// Reutiliza CheckoutSettlement y no recorre otras reservas.
/// </summary>
public sealed class OrderPaymentReconciliationService(
    ApplicationDbContext dbContext,
    IStripeCheckoutGateway gateway,
    IOrderStock orderStock,
    ILogger<OrderPaymentReconciliationService> logger) : IOrderPaymentReconciliation
{
    public async Task<OrderReconciliationResult> ReconcileAsync(
        Guid orderId,
        string? orderNumber,
        CancellationToken cancellationToken = default)
    {
        var number = orderNumber?.Trim() ?? string.Empty;
        if (orderId == Guid.Empty || number.Length == 0)
        {
            return Refused(OrderReconciliationRules.SessionMessage);
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
            return Refused(OrderReconciliationRules.SessionMessage);
        }

        var read = await gateway.ReadStoredSessionAsync(payment.StripeCheckoutSessionId, cancellationToken);
        if (read.Availability != StoredSessionAvailability.Retrieved || read.Session is null)
        {
            return Refused(read.Availability == StoredSessionAvailability.Missing
                ? OrderReconciliationRules.SessionMessage
                : OrderReconciliationRules.UnavailableMessage);
        }

        var now = DateTimeOffset.UtcNow;
        var reservation = await dbContext.StockReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var verdict = OrderReconciliationRules.Decide(Context(preview, payment, reservation, read.Session, now));
        if (verdict.Decision is OrderReconciliationDecision.Refuse or OrderReconciliationDecision.AlreadySettled)
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
            return Refused(OrderReconciliationRules.SessionMessage);
        }

        now = DateTimeOffset.UtcNow;
        verdict = OrderReconciliationRules.Decide(Context(current, currentPayment, currentReservation, read.Session, now));
        if (verdict.Decision is OrderReconciliationDecision.Refuse or OrderReconciliationDecision.AlreadySettled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Map(verdict);
        }

        if (currentReservation is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Refused(StripeWebhookRules.StockMessage);
        }

        if (verdict.Decision == OrderReconciliationDecision.Confirm)
        {
            await new CheckoutSettlement(orderStock).ConfirmPaidAsync(
                current,
                currentPayment,
                currentReservation,
                read.Session.PaymentIntentId!,
                now,
                cancellationToken);
        }
        else if (verdict.Decision == OrderReconciliationDecision.FinishHold)
        {
            await orderStock.ConfirmPaymentAsync(current.Id, now, cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
            return Refused(StripeWebhookRules.ClosedOrderMessage);
        }

        Record(current, currentPayment, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Pedido {OrderNumber} reconciliado.", current.Number);
        return new OrderReconciliationResult(OrderReconciliationResultKind.Confirmed, null);
    }

    private async Task<Order?> LoadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Include(order => order.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    private static Payment? SelectPayment(Order order)
    {
        var withSession = order.Payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.StripeCheckoutSessionId))
            .ToList();
        return withSession.Count == 1 ? withSession[0] : null;
    }

    private static OrderReconciliationContext Context(
        Order order,
        Payment payment,
        StockReservation? reservation,
        ObservedCheckoutSession session,
        DateTimeOffset now)
    {
        return new OrderReconciliationContext(
            order.Id,
            payment.StripeCheckoutSessionId,
            payment.StripePaymentIntentId,
            order.Status,
            payment.Status,
            order.TotalCents,
            payment.AmountCents,
            reservation?.Status,
            order.ReservationExpiresAt,
            now,
            session);
    }

    private static void Record(Order order, Payment payment, DateTimeOffset now)
    {
        var externalId = OrderReconciliationRules.EventIdFor(order.Id);
        if (payment.Events.Any(item => item.ExternalEventId == externalId))
        {
            return;
        }

        var recorded = order.RecordPaymentEvent(
            payment.Id,
            Guid.NewGuid(),
            externalId,
            OrderReconciliationRules.EventType,
            now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now);
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

    private static OrderReconciliationResult Map(OrderReconciliationVerdict verdict)
    {
        return verdict.Decision switch
        {
            OrderReconciliationDecision.AlreadySettled => new OrderReconciliationResult(
                OrderReconciliationResultKind.AlreadySettled,
                null),
            OrderReconciliationDecision.Confirm or OrderReconciliationDecision.FinishHold => new OrderReconciliationResult(
                OrderReconciliationResultKind.Confirmed,
                null),
            _ => Refused(verdict.Reason ?? StripeWebhookRules.ClosedOrderMessage)
        };
    }

    private static OrderReconciliationResult Refused(string reason)
    {
        return new OrderReconciliationResult(OrderReconciliationResultKind.Refused, reason);
    }
}
