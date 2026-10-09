using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Busca reservas vencidas y, fuera de cualquier transacción, pregunta a Stripe antes de tocar el stock.
/// Una sesión abierta, un cobro asíncrono o una respuesta fallida conservan la reserva.
/// </summary>
public sealed class ReservationSweepService(
    ApplicationDbContext dbContext,
    IStripeCheckoutGateway gateway,
    IOrderStock orderStock,
    ReservationSweepFailureProbe failures,
    IOptions<ReservationSweepOptions> options,
    ILogger<ReservationSweepService> logger) : IReservationSweep
{
    private const string PaidEventType = "reservation.sweep.paid";
    private const string AttentionEventType = "reservation.sweep.attention";

    public async Task<ReservationSweepResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(cancellationToken))
            {
                logger.LogInformation("Barrido de reservas omitido porque ya hay otro en curso.");
                return new ReservationSweepResult(0, 0, 0, 0, 0);
            }

            try
            {
                return await SweepAsync(cancellationToken);
            }
            finally
            {
                await UnlockAsync(cancellationToken);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<ReservationSweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var batch = Math.Clamp(options.Value.BatchSize, 1, 100);
        var now = DateTimeOffset.UtcNow;
        var ids = await (
                from order in dbContext.Orders.AsNoTracking()
                join reservation in dbContext.StockReservations.AsNoTracking() on order.Id equals reservation.OrderId
                where order.Status == OrderStatus.PendingPayment
                    && order.ReservationExpiresAt <= now
                    && (reservation.Status == StockReservationStatus.Reserved
                        || reservation.Status == StockReservationStatus.Confirming
                        || reservation.Status == StockReservationStatus.Released)
                orderby order.ReservationExpiresAt, order.Id
                select order.Id)
            .Take(batch)
            .ToListAsync(cancellationToken);

        var processed = 0;
        var released = 0;
        var confirmed = 0;
        var failed = 0;
        var incidents = 0;
        foreach (var orderId in ids)
        {
            processed++;
            try
            {
                var outcome = await ReconcileAsync(orderId, cancellationToken);
                switch (outcome)
                {
                    case SweepOutcome.Released:
                        released++;
                        break;
                    case SweepOutcome.Confirmed:
                        confirmed++;
                        break;
                    case SweepOutcome.Incident:
                        incidents++;
                        break;
                    case SweepOutcome.Failed:
                        failed++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(
                    "No se ha podido reconciliar el pedido {OrderId}. Tipo {ExceptionType}.",
                    orderId,
                    exception.GetType().Name);
            }
        }

        logger.LogInformation(
            "Barrido de reservas. Procesados {Processed}, liberados {Released}, confirmados {Confirmed}, incidencias {Incidents}, fallidos {Failed}.",
            processed,
            released,
            confirmed,
            incidents,
            failed);
        return new ReservationSweepResult(processed, released, confirmed, failed, incidents);
    }

    private async Task<SweepOutcome> ReconcileAsync(Guid orderId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var snapshot = await LoadSnapshotAsync(orderId, cancellationToken);
        if (snapshot is null || !IsCandidate(snapshot, DateTimeOffset.UtcNow))
        {
            logger.LogInformation("Pedido {OrderId} kept. Estado {OrderStatus}.", orderId, snapshot?.Status.ToString() ?? "Missing");
            return SweepOutcome.Kept;
        }

        StripeSessionInspection inspection;
        try
        {
            inspection = await gateway.InspectAsync(new StripeSessionQuery(orderId, snapshot.SessionId), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Stripe no ha respondido para el pedido {OrderId}. Tipo {ExceptionType}.",
                orderId,
                exception.GetType().Name);
            return SweepOutcome.Failed;
        }

        var sessionStored = !string.IsNullOrWhiteSpace(snapshot.SessionId);
        if (inspection.Fact == StripeCheckoutFact.Unavailable
            || ReservationSweepRules.WaitForMissingSession(sessionStored, inspection.Fact, DateTimeOffset.UtcNow, snapshot.ReservationExpiresAt))
        {
            var outcome = inspection.Fact == StripeCheckoutFact.Unavailable ? SweepOutcome.Failed : SweepOutcome.Kept;
            logger.LogInformation(
                "Pedido {OrderId} {Outcome}. Estado {OrderStatus}. Reserva {ReservationStatus}.",
                orderId,
                outcome == SweepOutcome.Failed ? "failed" : "kept",
                snapshot.Status,
                snapshot.ReservationStatus);
            return outcome;
        }

        if (inspection.Fact is StripeCheckoutFact.Open or StripeCheckoutFact.Pending
            && (sessionStored || string.IsNullOrWhiteSpace(inspection.SessionId)))
        {
            logger.LogInformation(
                "Pedido {OrderId} kept. Estado {OrderStatus}. Reserva {ReservationStatus}.",
                orderId,
                snapshot.Status,
                snapshot.ReservationStatus);
            return SweepOutcome.Kept;
        }

        return await SettleAsync(orderId, inspection, cancellationToken);
    }

    private async Task<SweepOutcome> SettleAsync(
        Guid orderId,
        StripeSessionInspection inspection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return SweepOutcome.Kept;
        }

        var now = DateTimeOffset.UtcNow;
        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .Include(item => item.Items)
            .ThenInclude(item => item.Components)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (reservation is null || order.Status == OrderStatus.Paid || now < order.ReservationExpiresAt)
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Pedido {OrderId} kept. Estado {OrderStatus}.", orderId, order.Status);
            return SweepOutcome.Kept;
        }

        if (reservation.Status == StockReservationStatus.Confirmed && inspection.Fact != StripeCheckoutFact.Paid)
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Pedido {OrderId} kept. Estado {OrderStatus}. Reserva Confirmed.", orderId, order.Status);
            return SweepOutcome.Kept;
        }

        var payment = order.Payments.SingleOrDefault(item => item.Status == PaymentStatus.Pending);
        var outcome = await ApplyAsync(order, payment, reservation, inspection, now, cancellationToken);
        if (outcome is SweepOutcome.Released or SweepOutcome.Confirmed && failures.ConsumeFailure())
        {
            throw new IOException("No se ha podido guardar la reconciliación.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Pedido {OrderId} {Outcome}. Estado {OrderStatus}. Reserva {ReservationStatus}.",
            orderId,
            OutcomeName(outcome),
            order.Status,
            reservation.Status);
        return outcome;
    }

    private async Task<SweepOutcome> ApplyAsync(
        Order order,
        Payment? payment,
        StockReservation reservation,
        StripeSessionInspection inspection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attention = inspection.AttentionReason;
        if (inspection.Fact == StripeCheckoutFact.Paid && payment is not null && attention is null)
        {
            attention = PaidSessionRules.Attention(
                inspection.AmountTotal,
                inspection.Currency,
                inspection.PaymentIntentId,
                inspection.ClientReferenceId,
                inspection.MetadataOrderId,
                order.Status,
                payment.Status,
                order.TotalCents,
                payment.AmountCents,
                order.Id,
                reservation.Status);
            if (payment.StripePaymentIntentId is not null
                && !string.Equals(payment.StripePaymentIntentId, inspection.PaymentIntentId, StringComparison.Ordinal))
            {
                attention = StripeWebhookRules.IntentMessage;
            }
        }

        if (reservation.Status == StockReservationStatus.Confirming && inspection.Fact != StripeCheckoutFact.Paid)
        {
            attention ??= ReservationSweepRules.ConfirmingMessage;
        }

        if (inspection.Fact == StripeCheckoutFact.Paid && attention is null)
        {
            payment ??= order.AddPayment(Guid.NewGuid(), now);
            attention = Remember(order, payment, inspection) ?? PaidSessionRules.Attention(
                inspection.AmountTotal,
                inspection.Currency,
                inspection.PaymentIntentId,
                inspection.ClientReferenceId,
                inspection.MetadataOrderId,
                order.Status,
                payment.Status,
                order.TotalCents,
                payment.AmountCents,
                order.Id,
                reservation.Status);
            if (attention is null)
            {
                await new CheckoutSettlement(orderStock).ConfirmPaidAsync(
                    order,
                    payment,
                    reservation,
                    inspection.PaymentIntentId!,
                    now,
                    cancellationToken);
                Record(order, payment, $"sweep:{inspection.SessionId ?? order.Id.ToString("N")}:paid", PaidEventType, null, now);
                return SweepOutcome.Confirmed;
            }
        }

        if (attention is not null || inspection.Fact == StripeCheckoutFact.Contradiction)
        {
            payment ??= order.AddPayment(Guid.NewGuid(), now);
            Remember(order, payment, inspection);
            Record(
                order,
                payment,
                $"sweep:{order.Id:N}:attention",
                AttentionEventType,
                attention ?? inspection.AttentionReason ?? ReservationSweepRules.UnresolvedMessage,
                now);
            return SweepOutcome.Incident;
        }

        if (inspection.Fact is StripeCheckoutFact.Open or StripeCheckoutFact.Pending)
        {
            if (payment is not null)
            {
                Remember(order, payment, inspection);
            }

            return SweepOutcome.Kept;
        }

        if (payment is not null)
        {
            Remember(order, payment, inspection);
        }

        var releasedNow = reservation.Status == StockReservationStatus.Reserved;
        if (releasedNow)
        {
            await orderStock.ReleaseAsync(order.Id, now, cancellationToken);
        }

        if (order.Status == OrderStatus.PendingPayment)
        {
            order.Expire(now);
        }

        return releasedNow ? SweepOutcome.Released : SweepOutcome.Kept;
    }

    private static string? Remember(Order order, Payment payment, StripeSessionInspection inspection)
    {
        if (string.IsNullOrWhiteSpace(inspection.SessionId))
        {
            return null;
        }

        if (!inspection.SessionId.StartsWith("cs_test_", StringComparison.Ordinal)
            || inspection.SessionId.Contains("live", StringComparison.OrdinalIgnoreCase))
        {
            return StripeCheckoutGuard.TestKeyMessage;
        }

        if (payment.StripeCheckoutSessionId is null)
        {
            order.AssignCheckoutSession(payment.Id, inspection.SessionId);
            return null;
        }

        if (!string.Equals(payment.StripeCheckoutSessionId, inspection.SessionId, StringComparison.Ordinal))
        {
            return StripeWebhookRules.ReferenceMessage;
        }

        return null;
    }

    private static void Record(
        Order order,
        Payment payment,
        string externalId,
        string eventType,
        string? attention,
        DateTimeOffset now)
    {
        if (payment.Events.Any(item => item.ExternalEventId == externalId))
        {
            return;
        }

        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), externalId, eventType, now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, attention);
    }

    private async Task<HoldSnapshot?> LoadSnapshotAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new { item.Status, item.ReservationExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        var reservation = await dbContext.StockReservations
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .Select(item => (StockReservationStatus?)item.Status)
            .SingleOrDefaultAsync(cancellationToken);
        var sessionId = await dbContext.Payments
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.Status == PaymentStatus.Pending)
            .Select(item => item.StripeCheckoutSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        return reservation is null
            ? null
            : new HoldSnapshot(order.Status, order.ReservationExpiresAt, reservation.Value, sessionId);
    }

    private static bool IsCandidate(HoldSnapshot snapshot, DateTimeOffset now)
    {
        return snapshot.Status == OrderStatus.PendingPayment
            && snapshot.ReservationExpiresAt <= now
            && snapshot.ReservationStatus is StockReservationStatus.Reserved
                or StockReservationStatus.Confirming
                or StockReservationStatus.Released;
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

    private async Task<bool> TryLockAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(610091, 6)";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool locked && locked;
    }

    private async Task UnlockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(610091, 6)";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("No se ha podido soltar el bloqueo del barrido. Tipo {ExceptionType}.", exception.GetType().Name);
        }
    }

    private static string OutcomeName(SweepOutcome outcome)
    {
        return outcome switch
        {
            SweepOutcome.Released => "released",
            SweepOutcome.Confirmed => "confirmed",
            SweepOutcome.Incident => "incident",
            SweepOutcome.Failed => "failed",
            _ => "kept"
        };
    }

    private enum SweepOutcome
    {
        Kept,
        Released,
        Confirmed,
        Incident,
        Failed
    }

    private sealed record HoldSnapshot(
        OrderStatus Status,
        DateTimeOffset ReservationExpiresAt,
        StockReservationStatus ReservationStatus,
        string? SessionId);
}
