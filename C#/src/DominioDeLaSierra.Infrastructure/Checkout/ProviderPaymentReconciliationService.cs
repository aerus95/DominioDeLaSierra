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
/// Concilia pagos pendientes de Redsys y PayPal. PayPal se lee en Orders v2 y no se captura.
/// Redsys no tiene consulta verificada: la operación silenciosa queda Unknown.
/// La antigüedad no libera stock. El proceso automático nace apagado.
/// </summary>
public sealed class ProviderPaymentReconciliationService(
    ApplicationDbContext dbContext,
    IPayPalObservations paypal,
    PayPalSettings payPalSettings,
    ReservationHoldIncidentRecorder incidents,
    IOptions<ReservationSweepOptions> options,
    ILogger<ProviderPaymentReconciliationService> logger) : IProviderPaymentReconciliation
{
    public Task<ProviderReconciliationBatchResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return RunOnceAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<ProviderReconciliationBatchResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(cancellationToken))
            {
                logger.LogInformation("Conciliación de Redsys y PayPal omitida porque ya hay otra en curso.");
                return new ProviderReconciliationBatchResult(0, 0, 0, 0, 0, 0);
            }

            try
            {
                return await RunAsync(now, cancellationToken);
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

    public async Task<IReadOnlyList<ProviderReconciliationRow>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await (
                from payment in dbContext.Payments.AsNoTracking()
                join order in dbContext.Orders.AsNoTracking() on payment.OrderId equals order.Id
                join reservation in dbContext.StockReservations.AsNoTracking() on order.Id equals reservation.OrderId
                where payment.Status == PaymentStatus.Pending
                    && order.Status == OrderStatus.PendingPayment
                    && (payment.Provider == PaymentProvider.Redsys || payment.Provider == PaymentProvider.PayPal)
                select new PendingRow(
                    order.Id,
                    order.Number,
                    payment.Id,
                    payment.Provider,
                    payment.RedsysOrderNumber ?? payment.ExternalOperationId,
                    payment.AmountCents,
                    payment.Currency,
                    reservation.ReservedAt))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var paymentIds = rows.Select(item => item.PaymentId).ToArray();
        var orderIds = rows.Select(item => item.OrderId).ToArray();
        var states = await dbContext.ProviderReconciliations
            .AsNoTracking()
            .Where(item => paymentIds.Contains(item.PaymentId))
            .ToListAsync(cancellationToken);
        var open = await dbContext.ReservationHoldIncidents
            .AsNoTracking()
            .Where(item => item.ResolvedAt == null && orderIds.Contains(item.OrderId))
            .ToListAsync(cancellationToken);
        var stateByPayment = states.ToDictionary(item => item.PaymentId);
        var incidentByOrder = open.ToDictionary(item => item.OrderId);

        return rows
            .Select(item => Present(item, stateByPayment, incidentByOrder, now))
            .OrderByDescending(item => item.IncidentSeverity == nameof(ReservationHoldSeverity.Critical))
            .ThenBy(item => item.ReservedAt)
            .ToArray();
    }

    public async Task<ProviderReconciliationActionResult> RecheckPayPalAsync(
        Guid orderId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return new ProviderReconciliationActionResult(false, ProviderReconciliationRules.NotPayPalMessage);
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.Provider == PaymentProvider.PayPal && item.Status == PaymentStatus.Pending)
            .Select(item => new { item.Id, item.ExternalOperationId })
            .SingleOrDefaultAsync(cancellationToken);
        var pending = payment is not null
            && await dbContext.Orders.AsNoTracking().AnyAsync(
                item => item.Id == orderId && item.Status == OrderStatus.PendingPayment,
                cancellationToken);
        if (!pending || payment is null)
        {
            return new ProviderReconciliationActionResult(false, ProviderReconciliationRules.NotPayPalMessage);
        }

        var now = DateTimeOffset.UtcNow;
        logger.LogInformation("Comprobación PayPal del pedido {OrderId} pedida por {ActorId}.", orderId, actorUserId);
        if (!payPalSettings.IsOperational)
        {
            await RememberAsync(
                payment.Id,
                orderId,
                PaymentProvider.PayPal,
                payment.ExternalOperationId,
                ProviderReconciliationRules.UnavailableEvidence,
                ProviderReconciliationRules.NotConfiguredMessage,
                now,
                cancellationToken);
            return new ProviderReconciliationActionResult(false, ProviderReconciliationRules.NotConfiguredMessage);
        }

        try
        {
            var observation = await paypal.ObserveAsync(
                orderId,
                ProviderReconciliationRules.AdminEventId(payment.Id, now),
                cancellationToken);
            await RememberAsync(
                payment.Id,
                orderId,
                PaymentProvider.PayPal,
                payment.ExternalOperationId,
                ProviderReconciliationRules.EvidenceOf(observation.Evidence),
                observation.Attention ?? ProviderReconciliationRules.KeptMessage,
                now,
                cancellationToken);
            return new ProviderReconciliationActionResult(true, MessageFor(observation));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "PayPal no ha respondido al pedido {OrderId}. Tipo {ExceptionType}.",
                orderId,
                exception.GetType().Name);
            dbContext.ChangeTracker.Clear();
            await RememberAsync(
                payment.Id,
                orderId,
                PaymentProvider.PayPal,
                payment.ExternalOperationId,
                ProviderReconciliationRules.UnavailableEvidence,
                ProviderReconciliationRules.UnreachableMessage,
                now,
                cancellationToken);
            return new ProviderReconciliationActionResult(false, ProviderReconciliationRules.UnreachableMessage);
        }
    }

    private async Task<ProviderReconciliationBatchResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await DueAsync(now, cancellationToken);
        var confirmed = 0;
        var released = 0;
        var kept = 0;
        var incidents = 0;
        var failed = 0;
        foreach (var item in due)
        {
            try
            {
                var outcome = item.Provider == PaymentProvider.PayPal
                    ? await ReconcilePayPalAsync(item, now, cancellationToken)
                    : await ReconcileRedsysAsync(item, now, cancellationToken);
                switch (outcome.Kind)
                {
                    case ItemKind.Confirmed:
                        confirmed++;
                        break;
                    case ItemKind.Released:
                        released++;
                        break;
                    case ItemKind.Failed:
                        failed++;
                        break;
                    default:
                        kept++;
                        break;
                }

                if (outcome.Incident)
                {
                    incidents++;
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
                    "No se ha podido conciliar el pedido {OrderId}. Tipo {ExceptionType}.",
                    item.OrderId,
                    exception.GetType().Name);
            }
        }

        logger.LogInformation(
            "Conciliación de Redsys y PayPal. Procesados {Processed}, confirmados {Confirmed}, liberados {Released}, conservados {Kept}, incidencias {Incidents}, fallidos {Failed}.",
            due.Count,
            confirmed,
            released,
            kept,
            incidents,
            failed);
        return new ProviderReconciliationBatchResult(due.Count, confirmed, released, kept, incidents, failed);
    }

    private async Task<ItemOutcome> ReconcilePayPalAsync(DuePayment item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!payPalSettings.IsOperational || !ProviderReconciliationRules.SupportsQuery(item.Provider))
        {
            await RememberAsync(
                item.PaymentId,
                item.OrderId,
                PaymentProvider.PayPal,
                item.ExternalReference,
                ProviderReconciliationRules.UnavailableEvidence,
                ProviderReconciliationRules.NotConfiguredMessage,
                now,
                cancellationToken);
            return ItemOutcome.Keep(false);
        }

        var attempt = await NextAttemptAsync(item.PaymentId, cancellationToken);
        PayPalObservation observation;
        try
        {
            observation = await paypal.ObserveAsync(
                item.OrderId,
                ProviderReconciliationRules.PayPalAttemptEventId(item.PaymentId, attempt),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "PayPal no ha respondido al pedido {OrderId}. Tipo {ExceptionType}.",
                item.OrderId,
                exception.GetType().Name);
            dbContext.ChangeTracker.Clear();
            await RememberAsync(
                item.PaymentId,
                item.OrderId,
                PaymentProvider.PayPal,
                item.ExternalReference,
                ProviderReconciliationRules.UnavailableEvidence,
                ProviderReconciliationRules.UnreachableMessage,
                now,
                cancellationToken);
            return ItemOutcome.Fail();
        }

        dbContext.ChangeTracker.Clear();
        await RememberAsync(
            item.PaymentId,
            item.OrderId,
            PaymentProvider.PayPal,
            item.ExternalReference,
            ProviderReconciliationRules.EvidenceOf(observation.Evidence),
            observation.Attention ?? ProviderReconciliationRules.KeptMessage,
            now,
            cancellationToken);
        if (observation.StockChanged && observation.Evidence == PaymentEvidence.Paid)
        {
            return ItemOutcome.Confirm();
        }

        if (observation.StockChanged && observation.Evidence == PaymentEvidence.ClosedUnpaid)
        {
            return ItemOutcome.Release();
        }

        var incident = observation.Evidence == PaymentEvidence.Paid
            && !observation.StockChanged
            && observation.Attention == PayPalRules.StockMessage;
        return ItemOutcome.Keep(incident);
    }

    private async Task<ItemOutcome> ReconcileRedsysAsync(DuePayment item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(item.OrderId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ItemOutcome.Keep(false);
        }

        var order = await dbContext.Orders
            .Include(entry => entry.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(entry => entry.Id == item.OrderId, cancellationToken);
        var payment = order.Payments.SingleOrDefault(entry => entry.Id == item.PaymentId);
        var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(entry => entry.OrderId == order.Id, cancellationToken);
        if (payment is null
            || payment.Status != PaymentStatus.Pending
            || payment.Provider != PaymentProvider.Redsys
            || order.Status != OrderStatus.PendingPayment)
        {
            await transaction.CommitAsync(cancellationToken);
            return ItemOutcome.Keep(false);
        }

        var state = await dbContext.ProviderReconciliations.SingleOrDefaultAsync(entry => entry.PaymentId == payment.Id, cancellationToken);
        if (state is not null && !ProviderReconciliationRules.IsDue(state.NextAttemptAt, now))
        {
            await transaction.CommitAsync(cancellationToken);
            return ItemOutcome.Keep(false);
        }

        var attempt = (state?.AttemptCount ?? 0) + 1;
        var eventId = ProviderReconciliationRules.RedsysAttemptEventId(payment.Id, attempt);
        if (payment.Events.All(entry => entry.ExternalEventId != eventId))
        {
            var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), eventId, ProviderReconciliationRules.RedsysEventType, now);
            order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, ProviderReconciliationRules.RedsysSilentMessage);
        }

        var incident = false;
        if (reservation?.Status is StockReservationStatus.Reserved or StockReservationStatus.Confirming)
        {
            incident = await incidents.EnsureHeldAsync(
                order.Id,
                PaymentProvider.Redsys,
                payment.RedsysOrderNumber ?? payment.ExternalOperationId,
                now,
                cancellationToken);
        }

        WriteState(
            state,
            payment.Id,
            order.Id,
            PaymentProvider.Redsys,
            payment.RedsysOrderNumber ?? payment.ExternalOperationId,
            nameof(PaymentEvidence.Unknown),
            ProviderReconciliationRules.RedsysSilentMessage,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Pedido Redsys {OrderId} conservado. Sin consulta remota.", order.Id);
        return ItemOutcome.Keep(incident);
    }

    private async Task RememberAsync(
        Guid paymentId,
        Guid orderId,
        PaymentProvider provider,
        string? externalReference,
        string evidence,
        string? attention,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var state = await dbContext.ProviderReconciliations.SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        WriteState(state, paymentId, orderId, provider, externalReference, evidence, attention, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void WriteState(
        ProviderReconciliation? state,
        Guid paymentId,
        Guid orderId,
        PaymentProvider provider,
        string? externalReference,
        string evidence,
        string? attention,
        DateTimeOffset now)
    {
        var reference = ReservationHoldIncident.OpaqueReference(externalReference);
        if (reference is null)
        {
            return;
        }

        if (state is null)
        {
            dbContext.ProviderReconciliations.Add(ProviderReconciliation.Start(
                Guid.NewGuid(),
                paymentId,
                orderId,
                provider,
                reference,
                evidence,
                now,
                ProviderReconciliationRules.NextAttemptAt(1, now),
                attention));
            return;
        }

        var attemptedAt = now < state.LastAttemptAt ? state.LastAttemptAt : now;
        state.Record(
            evidence,
            attemptedAt,
            ProviderReconciliationRules.NextAttemptAt(state.AttemptCount + 1, attemptedAt),
            reference,
            attention);
    }

    private async Task<int> NextAttemptAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var count = await dbContext.ProviderReconciliations
            .AsNoTracking()
            .Where(item => item.PaymentId == paymentId)
            .Select(item => (int?)item.AttemptCount)
            .SingleOrDefaultAsync(cancellationToken);
        return (count ?? 0) + 1;
    }

    private async Task<IReadOnlyList<DuePayment>> DueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batch = Math.Clamp(options.Value.BatchSize, 1, 100);
        var pending = await (
                from payment in dbContext.Payments.AsNoTracking()
                join order in dbContext.Orders.AsNoTracking() on payment.OrderId equals order.Id
                join reservation in dbContext.StockReservations.AsNoTracking() on order.Id equals reservation.OrderId
                where payment.Status == PaymentStatus.Pending
                    && order.Status == OrderStatus.PendingPayment
                    && (payment.Provider == PaymentProvider.Redsys || payment.Provider == PaymentProvider.PayPal)
                    && (reservation.Status == StockReservationStatus.Reserved
                        || reservation.Status == StockReservationStatus.Confirming
                        || reservation.Status == StockReservationStatus.Released)
                orderby reservation.ReservedAt, order.Id
                select new DuePayment(
                    payment.Id,
                    order.Id,
                    payment.Provider,
                    payment.RedsysOrderNumber ?? payment.ExternalOperationId,
                    reservation.ReservedAt))
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return [];
        }

        var ids = pending.Select(item => item.PaymentId).ToArray();
        var states = await dbContext.ProviderReconciliations
            .AsNoTracking()
            .Where(item => ids.Contains(item.PaymentId))
            .Select(item => new { item.PaymentId, item.NextAttemptAt })
            .ToListAsync(cancellationToken);
        var next = states.ToDictionary(item => item.PaymentId, item => item.NextAttemptAt);
        return pending
            .Where(item => ProviderReconciliationRules.IsDue(next.GetValueOrDefault(item.PaymentId), now) || !next.ContainsKey(item.PaymentId))
            .Take(batch)
            .ToArray();
    }

    private ProviderReconciliationRow Present(
        PendingRow item,
        IReadOnlyDictionary<Guid, ProviderReconciliation> states,
        IReadOnlyDictionary<Guid, ReservationHoldIncident> incidentsByOrder,
        DateTimeOffset now)
    {
        states.TryGetValue(item.PaymentId, out var state);
        incidentsByOrder.TryGetValue(item.OrderId, out var incident);
        var age = now - item.ReservedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var reference = ReservationHoldIncident.OpaqueReference(state?.ExternalReference)
            ?? ReservationHoldIncident.OpaqueReference(item.ExternalReference)
            ?? "—";
        var evidence = state?.Evidence ?? (item.Provider == PaymentProvider.Redsys
            ? nameof(PaymentEvidence.Unknown)
            : "Pendiente");
        return new ProviderReconciliationRow(
            item.OrderId,
            item.OrderNumber,
            item.Provider.ToString(),
            reference,
            item.AmountCents,
            item.Currency,
            item.ReservedAt,
            (long)age.TotalSeconds,
            evidence,
            state?.AttemptCount ?? 0,
            state?.LastAttemptAt,
            state?.NextAttemptAt,
            state?.Attention,
            incident?.Severity,
            incident?.Reconciliation,
            item.Provider == PaymentProvider.PayPal && payPalSettings.IsOperational);
    }

    private static string MessageFor(PayPalObservation observation)
    {
        if (observation.StockChanged && observation.Evidence == PaymentEvidence.Paid)
        {
            return ProviderReconciliationRules.ConfirmedMessage;
        }

        if (observation.StockChanged && observation.Evidence == PaymentEvidence.ClosedUnpaid)
        {
            return ProviderReconciliationRules.ReleasedMessage;
        }

        return ProviderReconciliationRules.KeptMessage;
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

    private async Task<bool> TryLockAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(610091, 9)";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool locked && locked;
    }

    private async Task UnlockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(610091, 9)";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("No se ha podido soltar el bloqueo de la conciliación. Tipo {ExceptionType}.", exception.GetType().Name);
        }
    }

    private sealed record DuePayment(
        Guid PaymentId,
        Guid OrderId,
        PaymentProvider Provider,
        string? ExternalReference,
        DateTimeOffset ReservedAt);

    private sealed record PendingRow(
        Guid OrderId,
        string OrderNumber,
        Guid PaymentId,
        PaymentProvider Provider,
        string? ExternalReference,
        long AmountCents,
        string Currency,
        DateTimeOffset ReservedAt);

    private enum ItemKind
    {
        Kept,
        Confirmed,
        Released,
        Failed
    }

    private readonly record struct ItemOutcome(ItemKind Kind, bool Incident)
    {
        public static ItemOutcome Keep(bool incident) => new(ItemKind.Kept, incident);

        public static ItemOutcome Confirm() => new(ItemKind.Confirmed, false);

        public static ItemOutcome Release() => new(ItemKind.Released, false);

        public static ItemOutcome Fail() => new(ItemKind.Failed, false);
    }
}
