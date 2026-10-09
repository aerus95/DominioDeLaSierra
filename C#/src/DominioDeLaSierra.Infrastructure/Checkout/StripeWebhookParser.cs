using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Verifica la firma en local. No llama a Stripe y no registra el cuerpo ni el secreto.
/// En local: stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded,checkout.session.async_payment_failed,checkout.session.expired --forward-to http://localhost:5141/api/v1/stripe/webhook
/// El whsec_ que imprime la CLI se pone en Stripe__WebhookSecret o STRIPE_WEBHOOK_SECRET, fuera del repositorio.
/// </summary>
public sealed class StripeWebhookParser(IConfiguration configuration) : IStripeWebhookParser
{
    public StripeWebhookNotice Parse(string payload, string? signatureHeader)
    {
        var secret = StripeCheckoutGuard.RequireTestWebhookSecret(
            configuration["Stripe:WebhookSecret"] ?? configuration["STRIPE_WEBHOOK_SECRET"]);
        if (string.IsNullOrWhiteSpace(payload)
            || payload.Length > StripeWebhookRules.MaxBodyBytes
            || string.IsNullOrWhiteSpace(signatureHeader))
        {
            throw new StripeWebhookSignatureException();
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, secret, 300, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            throw new StripeWebhookSignatureException();
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            return new StripeWebhookNotice(
                stripeEvent.Id,
                stripeEvent.Type,
                stripeEvent.Livemode,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        string? orderId = null;
        session.Metadata?.TryGetValue("order_id", out orderId);
        return new StripeWebhookNotice(
            stripeEvent.Id,
            stripeEvent.Type,
            stripeEvent.Livemode,
            session.Id,
            session.PaymentStatus,
            session.AmountTotal,
            session.Currency,
            session.PaymentIntentId ?? session.PaymentIntent?.Id,
            session.ClientReferenceId,
            orderId);
    }
}
