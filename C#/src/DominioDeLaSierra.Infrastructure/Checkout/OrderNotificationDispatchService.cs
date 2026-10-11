using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Reclama avisos con FOR UPDATE SKIP LOCKED y los entrega fuera de la transacción del pago.
/// Un fallo SMTP solo cambia la notificación. Si Mailpit acepta el mensaje y el proceso cae
/// antes de marcarlo Sent, el reintento puede enviar otra copia: SMTP no ofrece exactly-once.
/// </summary>
public sealed class OrderNotificationDispatchService(
    ApplicationDbContext dbContext,
    IOrderMailSender sender,
    IOptions<OrderNotificationOptions> options,
    ILogger<OrderNotificationDispatchService> logger) : IOrderNotificationDispatch
{
    public async Task<OrderNotificationDispatchResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.DispatchEnabled
            || !string.Equals(settings.Delivery, OrderNotificationDelivery.Capture, StringComparison.Ordinal))
        {
            return new OrderNotificationDispatchResult(0, 0, 0);
        }

        var now = UtcNow();
        var batch = Math.Clamp(settings.BatchSize, 1, 20);
        List<Guid> ids;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            ids = await LockDueAsync(now, batch, cancellationToken);
            if (ids.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new OrderNotificationDispatchResult(0, 0, 0);
            }

            var notifications = await dbContext.OrderNotifications
                .Where(notification => ids.Contains(notification.Id))
                .ToListAsync(cancellationToken);
            foreach (var notification in notifications)
            {
                notification.Claim(now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
        var lease = now.Add(OrderNotificationRules.ProcessingLease);
        var sent = 0;
        var failed = 0;
        foreach (var id in ids)
        {
            if (await SendOneAsync(id, lease, now, cancellationToken))
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new OrderNotificationDispatchResult(ids.Count, sent, failed);
    }

    private async Task<bool> SendOneAsync(
        Guid notificationId,
        DateTimeOffset lease,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PaidConfirmationContent? content = null;
        try
        {
            var current = await dbContext.OrderNotifications
                .AsNoTracking()
                .SingleAsync(notification => notification.Id == notificationId, cancellationToken);
            if (!OwnsLease(current, lease))
            {
                return false;
            }

            content = await ReadContentAsync(current.OrderId, cancellationToken);
            var mail = PaidConfirmationMail.Compose(content, $"{notificationId:D}@mailpit.local");
            var providerMessageId = await sender.SendAsync(mail, cancellationToken);
            var saved = await FinishAsync(
                notificationId,
                lease,
                owned => owned.MarkSent(now, providerMessageId),
                cancellationToken);
            if (saved)
            {
                logger.LogInformation(
                    "Aviso {NotificationId} del pedido {OrderNumber} enviado.",
                    notificationId,
                    content.Number);
            }

            return saved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            try
            {
                await FinishAsync(
                    notificationId,
                    lease,
                    owned => owned.RecordFailure(now, "El aviso no se ha podido enviar."),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("Aviso {NotificationId} no pudo registrar el fallo.", notificationId);
            }

            logger.LogWarning(
                "Aviso {NotificationId} del pedido {OrderNumber} no enviado.",
                notificationId,
                content?.Number ?? "desconocido");
            return false;
        }
    }

    private async Task<bool> FinishAsync(
        Guid notificationId,
        DateTimeOffset lease,
        Action<OrderNotification> apply,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockNotificationAsync(notificationId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.ChangeTracker.Clear();
        var notification = await dbContext.OrderNotifications
            .SingleAsync(item => item.Id == notificationId, cancellationToken);
        if (!OwnsLease(notification, lease))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        apply(notification);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return true;
    }

    private async Task<PaidConfirmationContent> ReadContentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        return new PaidConfirmationContent(
            order.Number,
            order.CustomerName,
            order.Email,
            order.Items
                .Select(item => new PaidConfirmationLine(
                    item.Name,
                    item.Reference,
                    item.Quantity,
                    item.UnitPriceCents,
                    item.LineTotalCents))
                .ToArray(),
            order.ShippingCents,
            order.TotalCents,
            order.AddressLine,
            order.PostalCode,
            order.City,
            order.Province,
            order.CountryCode == "ES" ? "España" : order.CountryCode,
            order.DeliveryNotes);
    }

    private async Task<List<Guid>> LockDueAsync(DateTimeOffset now, int batch, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("El envío de avisos no tiene una transacción.");
        command.CommandText = """
            SELECT "Id"
            FROM "OrderNotifications"
            WHERE (
                    "Status" IN ('Pending', 'Failed')
                    AND "NextAttemptAt" <= @now
                )
                OR (
                    "Status" = 'Processing'
                    AND "ProcessingUntil" <= @now
                )
            ORDER BY "NextAttemptAt"
            LIMIT @batch
            FOR UPDATE SKIP LOCKED
            """;
        var nowParameter = command.CreateParameter();
        nowParameter.ParameterName = "now";
        nowParameter.Value = now;
        command.Parameters.Add(nowParameter);
        var batchParameter = command.CreateParameter();
        batchParameter.ParameterName = "batch";
        batchParameter.Value = batch;
        command.Parameters.Add(batchParameter);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<bool> LockNotificationAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("El envío de avisos no tiene una transacción.");
        command.CommandText = """SELECT "Id" FROM "OrderNotifications" WHERE "Id" = @id FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = notificationId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private static bool OwnsLease(OrderNotification notification, DateTimeOffset lease)
    {
        return notification.Status == OrderNotificationStatus.Processing
            && notification.ProcessingUntil == lease;
    }

    private static DateTimeOffset UtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }
}
