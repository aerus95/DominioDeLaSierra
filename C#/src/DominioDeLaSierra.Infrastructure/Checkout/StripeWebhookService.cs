using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Confirma el cobro solo con la sesión guardada en el pago, el importe y la reserva.
/// checkout.session.expired no libera stock: un cobro posterior todavía puede confirmar,
/// y la caducidad automática queda para el proceso periódico.
/// </summary>
public sealed class StripeWebhookService(
    ApplicationDbContext dbContext,
    IStripeWebhookParser parser,
    IOrderStock orderStock,
    StripeWebhookFailureProbe failures,
    ILogger<StripeWebhookService> logger) : IStripeWebhooks
{
    private static readonly HashSet<string> HandledTypes = new(StringComparer.Ordinal)
    {
        "checkout.session.completed",
        "checkout.session.async_payment_succeeded",
        "checkout.session.async_payment_failed",
        "checkout.session.expired"
    };

    public async Task<StripeWebhookResult> ReceiveAsync(
        string? payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        var notice = parser.Parse(payload ?? string.Empty, signatureHeader);
        if (notice.LiveMode || IsLiveIdentifier(notice.SessionId) || IsLiveIdentifier(notice.PaymentIntentId))
        {
            throw new StripeCheckoutException(StripeCheckoutGuard.TestKeyMessage);
        }

        if (!HandledTypes.Contains(notice.EventType) || string.IsNullOrWhiteSpace(notice.SessionId))
        {
            logger.LogInformation("Webhook {EventId} ignorado.", notice.EventId);
            return new StripeWebhookResult(StripeWebhookRules.Ignored);
        }

        if (!notice.SessionId.StartsWith("cs_test_", StringComparison.Ordinal))
        {
            throw new StripeCheckoutException(StripeCheckoutGuard.TestKeyMessage);
        }

        dbContext.ChangeTracker.Clear();
        var located = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.StripeCheckoutSessionId == notice.SessionId)
            .Select(payment => new { payment.OrderId })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null)
        {
            logger.LogInformation("Webhook {EventId} sin sesión conocida.", notice.EventId);
            return new StripeWebhookResult(StripeWebhookRules.Ignored);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(located.OrderId, cancellationToken))
        {
            logger.LogInformation("Webhook {EventId} sin pedido.", notice.EventId);
            return new StripeWebhookResult(StripeWebhookRules.Ignored);
        }

        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == located.OrderId, cancellationToken);
        var payment = order.Payments.Single(item => item.StripeCheckoutSessionId == notice.SessionId);
        if (payment.Events.Any(item => item.ExternalEventId == notice.EventId && item.ProcessedAt is not null))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Webhook {EventId} duplicado.", notice.EventId);
            return new StripeWebhookResult(StripeWebhookRules.Duplicate);
        }

        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == order.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var wasPaid = order.Status == OrderStatus.Paid;
        var attention = AttentionReason(notice, order, payment, reservation);
        var confirmed = false;
        if (attention is null && CanConfirm(notice) && !wasPaid)
        {
            await ConfirmAsync(order, payment, notice, reservation!, now, cancellationToken);
            confirmed = true;
        }

        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), notice.EventId, notice.EventType, now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, attention);
        if (failures.ConsumeFailure())
        {
            throw new IOException(StripeWebhookRules.UnavailableMessage);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEvent(exception))
        {
            logger.LogInformation("Webhook {EventId} duplicado.", notice.EventId);
            return new StripeWebhookResult(StripeWebhookRules.Duplicate);
        }

        var disposition = attention is not null
            ? StripeWebhookRules.NeedsReconciliation
            : confirmed ? StripeWebhookRules.Confirmed : StripeWebhookRules.Ignored;
        logger.LogInformation("Webhook {EventId} {Disposition}.", notice.EventId, disposition);
        return new StripeWebhookResult(disposition);
    }

    private async Task ConfirmAsync(
        Order order,
        Payment payment,
        StripeWebhookNotice notice,
        StockReservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await new CheckoutSettlement(orderStock).ConfirmPaidAsync(
            order,
            payment,
            reservation,
            notice.PaymentIntentId!,
            now,
            cancellationToken);
    }

    private static string? AttentionReason(
        StripeWebhookNotice notice,
        Order order,
        Payment payment,
        StockReservation? reservation)
    {
        if (!CanConfirm(notice) || order.Status == OrderStatus.Paid)
        {
            return null;
        }

        return PaidSessionRules.Attention(
            notice.AmountTotal,
            notice.Currency,
            notice.PaymentIntentId,
            notice.ClientReferenceId,
            notice.MetadataOrderId,
            order.Status,
            payment.Status,
            order.TotalCents,
            payment.AmountCents,
            order.Id,
            reservation?.Status);
    }

    private static bool CanConfirm(StripeWebhookNotice notice)
    {
        return notice.EventType is "checkout.session.completed" or "checkout.session.async_payment_succeeded"
            && string.Equals(notice.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
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
            ?? throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        command.CommandText = """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = orderId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static bool IsLiveIdentifier(string? value)
    {
        return value is not null && value.Contains("live", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUniqueEvent(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgres.ConstraintName, "IX_PaymentEvents_ExternalEventId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
