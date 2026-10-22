using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Abre o actualiza la incidencia abierta de un pedido. No la cierra:
/// el cierre verificado lo pide quien ya asentó o liberó con evidencia.
/// </summary>
public sealed class ReservationHoldIncidentRecorder(
    ApplicationDbContext dbContext,
    IOptions<ReservationSweepOptions> options)
{
    public Task<bool> EnsurePaidUnsettledAsync(
        Guid orderId,
        PaymentProvider provider,
        string? externalReference,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return EnsureAsync(orderId, provider, externalReference, now, paidUnsettled: true, cancellationToken);
    }

    public async Task EnsureBlockedReleaseAsync(
        Guid orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (await OpenIncidentAsync(orderId, cancellationToken) is not null)
        {
            return;
        }

        var reservedAt = await dbContext.StockReservations
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .Select(item => (DateTimeOffset?)item.ReservedAt)
            .SingleOrDefaultAsync(cancellationToken) ?? now;
        var age = now - reservedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .Select(item => new { item.Provider, item.ExternalOperationId })
            .FirstOrDefaultAsync(cancellationToken);
        dbContext.ReservationHoldIncidents.Add(ReservationHoldIncident.Open(
            Guid.NewGuid(),
            orderId,
            payment?.Provider,
            nameof(ReservationHoldSeverity.Held),
            nameof(ReservationHoldReconciliation.Holding),
            Seconds(age),
            now,
            now.AddHours(1),
            payment?.ExternalOperationId));
    }

    public Task<bool> EnsureHeldAsync(
        Guid orderId,
        PaymentProvider provider,
        string? externalReference,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return EnsureAsync(orderId, provider, externalReference, now, paidUnsettled: false, cancellationToken);
    }

    public async Task ResolveVerifiedAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var incident = await OpenIncidentAsync(orderId, cancellationToken);
        incident?.ResolveVerified(now);
    }

    private async Task<bool> EnsureAsync(
        Guid orderId,
        PaymentProvider provider,
        string? externalReference,
        DateTimeOffset now,
        bool paidUnsettled,
        CancellationToken cancellationToken)
    {
        var threshold = ReservationHoldAlertRules.Threshold(options.Value.HoldAlertHours);
        var reservedAt = await dbContext.StockReservations
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .Select(item => (DateTimeOffset?)item.ReservedAt)
            .SingleOrDefaultAsync(cancellationToken) ?? now;
        var age = now - reservedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var decision = ReservationHoldAlertRules.Decide(age, threshold, paidUnsettled);
        if (!decision.Open)
        {
            return false;
        }

        var incident = await OpenIncidentAsync(orderId, cancellationToken);
        var nextReview = now.Add(threshold);
        if (incident is null)
        {
            if (await SuppressedAsync(orderId, decision, cancellationToken))
            {
                return false;
            }

            dbContext.ReservationHoldIncidents.Add(ReservationHoldIncident.Open(
                Guid.NewGuid(),
                orderId,
                provider,
                decision.Severity.ToString(),
                decision.Reconciliation.ToString(),
                Seconds(age),
                now,
                nextReview,
                externalReference));
            return true;
        }

        incident.RememberReference(externalReference);
        if (incident.Severity == nameof(ReservationHoldSeverity.Critical)
            && decision.Severity != ReservationHoldSeverity.Critical)
        {
            return false;
        }

        var upgrade = decision.Severity == ReservationHoldSeverity.Critical
            && incident.Severity != nameof(ReservationHoldSeverity.Critical);
        if (!upgrade && !incident.AwaitingReview(now))
        {
            return false;
        }

        incident.Review(
            provider,
            decision.Severity.ToString(),
            decision.Reconciliation.ToString(),
            Seconds(age),
            now,
            nextReview,
            externalReference);
        return upgrade;
    }

    private async Task<ReservationHoldIncident?> OpenIncidentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await dbContext.ReservationHoldIncidents
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.ResolvedAt == null, cancellationToken);
    }

    private async Task<bool> SuppressedAsync(
        Guid orderId,
        ReservationHoldDecision decision,
        CancellationToken cancellationToken)
    {
        var closures = await dbContext.ReservationHoldIncidents
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.Resolution == nameof(ReservationHoldResolution.Administrative))
            .Select(item => item.Severity)
            .ToListAsync(cancellationToken);
        if (closures.Count == 0)
        {
            return false;
        }

        if (decision.Severity == ReservationHoldSeverity.Critical)
        {
            return closures.Contains(nameof(ReservationHoldSeverity.Critical));
        }

        return true;
    }

    private static long Seconds(TimeSpan age)
    {
        var seconds = (long)age.TotalSeconds;
        return seconds < 0 ? 0 : seconds;
    }
}
