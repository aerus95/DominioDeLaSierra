using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public sealed class StripeCheckoutGateway(IConfiguration configuration, ILogger<StripeCheckoutGateway> logger) : IStripeCheckoutGateway
{
    public async Task<CreatedStripeCheckout> CreateAsync(
        StripeCheckoutDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var secret = StripeCheckoutGuard.RequireTestSecret(configuration["Stripe:SecretKey"] ?? configuration["STRIPE_SECRET_KEY"]);
        var options = StripeCheckoutRequest.Create(draft);
        try
        {
            var service = new SessionService(new StripeClient(secret));
            var session = await service.CreateAsync(
                options,
                new RequestOptions { IdempotencyKey = idempotencyKey },
                cancellationToken);
            if (session.AmountTotal is null
                || string.IsNullOrWhiteSpace(session.Url)
                || string.IsNullOrWhiteSpace(session.Id)
                || !session.Id.StartsWith("cs_test_", StringComparison.Ordinal))
            {
                throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
            }

            return new CreatedStripeCheckout(session.Id, session.Url, session.AmountTotal.Value, session.ExpiresAt);
        }
        catch (StripeCheckoutException)
        {
            throw;
        }
        catch (StripeException exception)
        {
            logger.LogWarning("Stripe no ha creado la sesión de pruebas. Código {StripeErrorCode}.", exception.StripeError?.Code);
            throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
        }
    }

    public async Task<StripeSessionInspection> InspectAsync(
        StripeSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = StripeCheckoutGuard.RequireTestSecret(configuration["Stripe:SecretKey"] ?? configuration["STRIPE_SECRET_KEY"]);
            var service = new SessionService(new StripeClient(secret));
            if (!string.IsNullOrWhiteSpace(query.SessionId))
            {
                try
                {
                    var stored = await ReadAsync(service, query.SessionId, cancellationToken);
                    return StripeCheckoutFacts.Combine([Observe(stored)], query.OrderId);
                }
                catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
                {
                }
            }

            var matches = await FindByOrderAsync(service, query.OrderId, cancellationToken);
            if (matches is null)
            {
                return StripeSessionInspection.Unavailable();
            }

            if (matches.Count == 0)
            {
                return StripeSessionInspection.NotFound();
            }

            var observed = new List<ObservedCheckoutSession>(matches.Count);
            foreach (var id in matches)
            {
                observed.Add(Observe(await ReadAsync(service, id, cancellationToken)));
            }

            return StripeCheckoutFacts.Combine(observed, query.OrderId);
        }
        catch (StripeCheckoutException)
        {
            logger.LogWarning("Stripe no está disponible para reconciliar el pedido {OrderId}.", query.OrderId);
            return StripeSessionInspection.Unavailable();
        }
        catch (StripeException exception)
        {
            logger.LogWarning(
                "Stripe no ha respondido al reconciliar el pedido {OrderId}. Código {StripeErrorCode}.",
                query.OrderId,
                exception.StripeError?.Code);
            return StripeSessionInspection.Unavailable();
        }
    }

    private static async Task<IReadOnlyList<string>?> FindByOrderAsync(
        SessionService service,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var reference = orderId.ToString("D");
        var found = new List<string>();
        string? startingAfter = null;
        for (var page = 0; page < 3; page++)
        {
            var list = await service.ListAsync(
                new SessionListOptions
                {
                    Limit = 100,
                    StartingAfter = startingAfter
                },
                cancellationToken: cancellationToken);
            foreach (var session in list.Data)
            {
                var metadataMatches = session.Metadata is not null
                    && session.Metadata.TryGetValue("order_id", out var metadataOrderId)
                    && string.Equals(metadataOrderId, reference, StringComparison.Ordinal);
                if (string.Equals(session.ClientReferenceId, reference, StringComparison.Ordinal) || metadataMatches)
                {
                    found.Add(session.Id);
                }
            }

            if (found.Count > 0 || !list.HasMore || list.Data.Count == 0)
            {
                return found;
            }

            startingAfter = list.Data[^1].Id;
        }

        return null;
    }

    private static Task<Session> ReadAsync(SessionService service, string sessionId, CancellationToken cancellationToken)
    {
        return service.GetAsync(
            sessionId,
            new SessionGetOptions { Expand = ["payment_intent"] },
            cancellationToken: cancellationToken);
    }

    private static ObservedCheckoutSession Observe(Session session)
    {
        string? metadataOrderId = null;
        session.Metadata?.TryGetValue("order_id", out metadataOrderId);
        return new ObservedCheckoutSession(
            session.Id,
            session.Status,
            session.PaymentStatus,
            session.PaymentIntentId ?? session.PaymentIntent?.Id,
            session.PaymentIntent?.Status,
            session.AmountTotal,
            session.Currency,
            session.Livemode,
            session.ClientReferenceId,
            metadataOrderId);
    }
}
