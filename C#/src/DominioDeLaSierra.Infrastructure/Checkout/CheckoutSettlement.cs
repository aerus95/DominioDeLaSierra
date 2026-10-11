using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Confirma un cobro ya contrastado. El webhook, la reconciliación y el barrido usan el mismo camino,
/// dentro de una transacción que el llamador ya tiene abierta. La confirmación pendiente se añade
/// al pedido solo cuando este método termina, y el llamador la guarda con el pago. Si lanza,
/// el llamador no guarda y la transacción revierte el cobro.
/// </summary>
sealed class CheckoutSettlement(IOrderStock orderStock)
{
    public async Task ConfirmPaidAsync(
        Order order,
        Payment payment,
        StockReservation reservation,
        string paymentIntentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (reservation.Status is StockReservationStatus.Reserved or StockReservationStatus.Confirming)
        {
            await orderStock.BeginPaymentConfirmationAsync(order.Id, now, cancellationToken);
        }

        if (payment.StripePaymentIntentId is null)
        {
            order.AssignPaymentIntent(payment.Id, paymentIntentId);
        }
        else if (!string.Equals(payment.StripePaymentIntentId, paymentIntentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(StripeWebhookRules.IntentMessage);
        }

        order.MarkPaid(payment.Id, now);
        if (reservation.Status != StockReservationStatus.Confirmed)
        {
            await orderStock.ConfirmPaymentAsync(order.Id, now, cancellationToken);
        }

        order.RecordPaidConfirmation(now);
    }
}
