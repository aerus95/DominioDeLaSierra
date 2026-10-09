using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public static class PaidSessionRules
{
    public static string? Attention(
        long? amountTotal,
        string? currency,
        string? paymentIntentId,
        string? clientReferenceId,
        string? metadataOrderId,
        OrderStatus orderStatus,
        PaymentStatus paymentStatus,
        long orderTotalCents,
        long paymentAmountCents,
        Guid orderId,
        StockReservationStatus? reservationStatus)
    {
        if (amountTotal != orderTotalCents || amountTotal != paymentAmountCents)
        {
            return StripeWebhookRules.AmountMessage;
        }

        if (!string.Equals(currency, "eur", StringComparison.OrdinalIgnoreCase))
        {
            return StripeWebhookRules.CurrencyMessage;
        }

        if (string.IsNullOrWhiteSpace(paymentIntentId)
            || !paymentIntentId.StartsWith("pi_test_", StringComparison.Ordinal)
            || paymentIntentId.Contains("live", StringComparison.OrdinalIgnoreCase))
        {
            return StripeWebhookRules.IntentMessage;
        }

        if (RefersToAnotherOrder(clientReferenceId, orderId) || RefersToAnotherOrder(metadataOrderId, orderId))
        {
            return StripeWebhookRules.ReferenceMessage;
        }

        if (orderStatus is OrderStatus.Cancelled or OrderStatus.Expired || paymentStatus != PaymentStatus.Pending)
        {
            return StripeWebhookRules.ClosedOrderMessage;
        }

        if (reservationStatus is not (StockReservationStatus.Reserved or StockReservationStatus.Confirming or StockReservationStatus.Confirmed))
        {
            return StripeWebhookRules.StockMessage;
        }

        return null;
    }

    private static bool RefersToAnotherOrder(string? value, Guid orderId)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (!Guid.TryParse(value, out var referenced) || referenced != orderId);
    }
}
