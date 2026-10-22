using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Registra una incidencia cuando una reserva sigue retenida más allá del umbral,
/// o de inmediato si un cobro confirmado no se puede asentar. No libera stock,
/// no cancela pedidos y no cambia el estado del pago. No envía correo al cliente.
/// </summary>
public sealed class ReservationHoldAlertService(
    ApplicationDbContext dbContext,
    IOptions<ReservationSweepOptions> options,
    ILogger<ReservationHoldAlertService> logger) : IReservationHoldAlerts
{
    public Task<ReservationHoldAlertResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return RunOnceAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<bool> ResolveAdministrativelyAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            return false;
        }

        var incident = await dbContext.ReservationHoldIncidents
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.ResolvedAt == null, cancellationToken);
        if (incident is null)
        {
            return false;
        }

        incident.ResolveAdministratively(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Incidencia de reserva del pedido {OrderId} revisada.", orderId);
        return true;
    }

    public async Task<ReservationHoldAlertResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(cancellationToken))
            {
                logger.LogInformation("Revisión de reservas retenidas omitida porque ya hay otra en curso.");
                return new ReservationHoldAlertResult(0, 0, 0, 0);
            }

            try
            {
                return await ReviewAsync(now, cancellationToken);
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

    private async Task<ReservationHoldAlertResult> ReviewAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var threshold = ReservationHoldAlertRules.Threshold(options.Value.HoldAlertHours);
        var held = await dbContext.StockReservations
            .AsNoTracking()
            .Where(item => item.Status == StockReservationStatus.Reserved || item.Status == StockReservationStatus.Confirming)
            .Select(item => new HeldReservation(item.OrderId, item.ReservedAt))
            .ToListAsync(cancellationToken);
        var criticalIds = (await dbContext.PaymentEvents
            .AsNoTracking()
            .Where(item => item.AttentionReason == StripeWebhookRules.StockMessage
                || item.AttentionReason == StripeWebhookRules.ClosedOrderMessage
                || item.AttentionReason == PayPalRules.StockMessage
                || item.AttentionReason == RedsysNotificationRules.StockMessage)
            .Select(item => item.Payment.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var open = await dbContext.ReservationHoldIncidents
            .Where(item => item.ResolvedAt == null)
            .ToListAsync(cancellationToken);
        var orderIds = held.Select(item => item.OrderId)
            .Concat(criticalIds)
            .Concat(open.Select(item => item.OrderId))
            .Distinct()
            .ToArray();
        if (orderIds.Length == 0)
        {
            return new ReservationHoldAlertResult(0, 0, 0, 0);
        }

        var reservedAt = held.ToDictionary(item => item.OrderId, item => item.ReservedAt);
        var missingReservationIds = orderIds.Where(id => !reservedAt.ContainsKey(id)).ToArray();
        if (missingReservationIds.Length > 0)
        {
            var extra = await dbContext.StockReservations
                .AsNoTracking()
                .Where(item => missingReservationIds.Contains(item.OrderId))
                .Select(item => new { item.OrderId, item.ReservedAt })
                .ToListAsync(cancellationToken);
            foreach (var item in extra)
            {
                reservedAt[item.OrderId] = item.ReservedAt;
            }
        }

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .Select(item => new { item.OrderId, item.Provider, item.Status, item.CreatedAt, item.ExternalOperationId })
            .ToListAsync(cancellationToken);
        var choices = payments
            .GroupBy(item => item.OrderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Status == PaymentStatus.Pending).ThenByDescending(item => item.CreatedAt).First());
        var closedAdministratively = await dbContext.ReservationHoldIncidents
            .AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId) && item.Resolution == nameof(ReservationHoldResolution.Administrative))
            .Select(item => new AdministrativeClosure(item.OrderId, item.Severity))
            .ToListAsync(cancellationToken);
        var opened = 0;
        var reviewed = 0;
        var unchanged = 0;
        var resolved = 0;
        var openByOrder = open.ToDictionary(item => item.OrderId);

        foreach (var orderId in orderIds)
        {
            var age = reservedAt.TryGetValue(orderId, out var started) ? now - started : TimeSpan.Zero;
            var paidUnsettled = criticalIds.Contains(orderId);
            var stillHeld = held.Any(item => item.OrderId == orderId);
            var due = stillHeld && age >= threshold;
            var decision = ReservationHoldAlertRules.Decide(due ? age : TimeSpan.Zero, threshold, paidUnsettled);
            var applicable = paidUnsettled || due;
            PaymentProvider? knownProvider = null;
            string? knownReference = null;
            if (choices.TryGetValue(orderId, out var choice))
            {
                knownProvider = choice.Provider;
                knownReference = choice.ExternalOperationId;
            }

            if (openByOrder.TryGetValue(orderId, out var incident))
            {
                if (!applicable)
                {
                    incident.ResolveVerified(now);
                    resolved++;
                    logger.LogInformation("Incidencia de reserva del pedido {OrderId} resuelta.", orderId);
                    continue;
                }

                var upgrade = paidUnsettled && incident.Severity != nameof(ReservationHoldSeverity.Critical);
                if (!upgrade && !incident.AwaitingReview(now))
                {
                    unchanged++;
                    continue;
                }

                incident.Review(
                    knownProvider,
                    decision.Severity.ToString(),
                    decision.Reconciliation.ToString(),
                    Seconds(age),
                    now,
                    now.Add(threshold),
                    knownReference);
                reviewed++;
                logger.LogInformation(
                    "Incidencia de reserva del pedido {OrderId} actualizada. Gravedad {Severity}.",
                    orderId,
                    decision.Severity);
                continue;
            }

            if (!applicable || Suppressed(closedAdministratively.Where(item => item.OrderId == orderId).ToArray(), decision))
            {
                continue;
            }

            dbContext.ReservationHoldIncidents.Add(ReservationHoldIncident.Open(
                Guid.NewGuid(),
                orderId,
                knownProvider,
                decision.Severity.ToString(),
                decision.Reconciliation.ToString(),
                Seconds(age),
                now,
                now.Add(threshold),
                knownReference));
            opened++;
            logger.LogInformation(
                "Incidencia de reserva del pedido {OrderId}. Gravedad {Severity}. Antigüedad {AgeSeconds} segundos.",
                orderId,
                decision.Severity,
                Seconds(age));
        }

        if (opened + reviewed + resolved > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ReservationHoldAlertResult(opened, reviewed, unchanged, resolved);
    }

    private static bool Suppressed(IReadOnlyList<AdministrativeClosure> closures, ReservationHoldDecision decision)
    {
        if (closures.Count == 0)
        {
            return false;
        }

        if (decision.Severity == ReservationHoldSeverity.Critical)
        {
            return closures.Any(item => item.Severity == nameof(ReservationHoldSeverity.Critical));
        }

        return true;
    }

    private static long Seconds(TimeSpan age)
    {
        var seconds = (long)age.TotalSeconds;
        return seconds < 0 ? 0 : seconds;
    }

    private async Task<bool> TryLockAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(610091, 7)";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool locked && locked;
    }

    private async Task UnlockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(610091, 7)";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("No se ha podido soltar el bloqueo de las incidencias de reserva. Tipo {ExceptionType}.", exception.GetType().Name);
        }
    }

    private sealed record HeldReservation(Guid OrderId, DateTimeOffset ReservedAt);

    private sealed record AdministrativeClosure(Guid OrderId, string Severity);
}
