using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Lee la orden en PayPal. Si la llamada falla o el estado no está documentado, la evidencia es Unknown
/// y la reserva se conserva.
/// </summary>
public sealed class PayPalPaymentStatusQuery(
    ApplicationDbContext dbContext,
    IPayPalOrders paypal,
    PayPalSettings settings) : IPaymentStatusQuery
{
    public PaymentProvider Provider => PaymentProvider.PayPal;

    public bool IsConfigured => settings.IsOperational;

    public async Task<PaymentEvidence> InspectAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!settings.IsOperational)
        {
            return PaymentEvidence.Unknown;
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(item => item.OrderId == orderId && item.Provider == PaymentProvider.PayPal && item.Status == PaymentStatus.Pending)
            .Select(item => new { item.ExternalOperationId, item.AmountCents })
            .SingleOrDefaultAsync(cancellationToken);
        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new { item.Number, item.TotalCents })
            .SingleOrDefaultAsync(cancellationToken);
        if (payment?.ExternalOperationId is null || order is null)
        {
            return PaymentEvidence.Unknown;
        }

        try
        {
            var snapshot = await paypal.GetAsync(payment.ExternalOperationId, cancellationToken);
            return PayPalProtocol.Interpret(snapshot, orderId, order.Number, order.TotalCents).Evidence;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PaymentEvidence.Unknown;
        }
    }
}
