using DominioDeLaSierra.Application.Checkout;
using Stripe;
using Stripe.Checkout;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public static class StripeCheckoutRequest
{
    public static SessionCreateOptions Create(StripeCheckoutDraft draft)
    {
        var lines = draft.Lines;
        long sum = 0;
        foreach (var line in lines)
        {
            sum = checked(sum + (line.UnitAmountCents * line.Quantity));
        }

        if (sum != draft.AmountCents || draft.AmountCents < 0)
        {
            throw new InvalidOperationException("El importe del pago no coincide con el pedido.");
        }

        return new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = draft.OrderId.ToString("D"),
            SuccessUrl = draft.SuccessUrl,
            CancelUrl = draft.CancelUrl,
            ExpiresAt = draft.ExpiresAt.UtcDateTime,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = false },
            Metadata = Metadata(draft),
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Metadata = Metadata(draft)
            },
            LineItems = lines.Select(line => new SessionLineItemOptions
            {
                Quantity = line.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "eur",
                    UnitAmount = line.UnitAmountCents,
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = line.Name
                    }
                }
            }).ToList()
        };
    }

    private static Dictionary<string, string> Metadata(StripeCheckoutDraft draft)
    {
        return new Dictionary<string, string>
        {
            ["order_id"] = draft.OrderId.ToString("D"),
            ["order_number"] = draft.OrderNumber
        };
    }
}
