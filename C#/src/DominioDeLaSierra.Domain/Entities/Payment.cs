using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Payment
{
    public const int StripeIdMaxLength = 255;
    public const int CheckoutUrlMaxLength = 2048;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = OrderAmounts.Currency;
    public string? StripeCheckoutSessionId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTimeOffset? CheckoutExpiresAt { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    public Order Order { get; private set; } = null!;
    public IReadOnlyList<PaymentEvent> Events => events;

    private readonly List<PaymentEvent> events = [];

    private Payment()
    {
    }

    internal Payment(Guid id, Order order, long amountCents, DateTimeOffset createdAt)
    {
        OrderText.RequireId(id, "El pago");
        if (amountCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents), "El importe del pago no es válido.");
        }

        Id = id;
        Order = order;
        OrderId = order.Id;
        Status = PaymentStatus.Pending;
        AmountCents = amountCents;
        Currency = OrderAmounts.Currency;
        CreatedAt = createdAt;
    }

    internal void AssignCheckoutSession(string sessionId)
    {
        EnsurePending();
        if (StripeCheckoutSessionId is not null)
        {
            throw new InvalidOperationException("La sesión de pago ya está asignada.");
        }

        StripeCheckoutSessionId = OrderText.RequireToken(sessionId, StripeIdMaxLength, "La sesión de pago");
    }

    internal void AssignHostedCheckout(string sessionId, string checkoutUrl, DateTimeOffset expiresAt)
    {
        EnsurePending();
        if (StripeCheckoutSessionId is not null)
        {
            throw new InvalidOperationException("La sesión de pago ya está asignada.");
        }

        var session = OrderText.RequireToken(sessionId, StripeIdMaxLength, "La sesión de pago");
        if (!session.StartsWith("cs_test_", StringComparison.Ordinal) || session.Contains("live", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La sesión de pago no es de pruebas.");
        }

        var url = OrderText.RequireToken(checkoutUrl, CheckoutUrlMaxLength, "La dirección de pago");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.Equals(parsed.Host, "checkout.stripe.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La dirección de pago no es válida.");
        }

        if (expiresAt <= CreatedAt)
        {
            throw new ArgumentException("La caducidad del pago no es válida.");
        }

        StripeCheckoutSessionId = session;
        CheckoutUrl = url;
        CheckoutExpiresAt = expiresAt;
    }

    internal void AssignPaymentIntent(string paymentIntentId)
    {
        EnsurePending();
        if (StripePaymentIntentId is not null)
        {
            throw new InvalidOperationException("El intento de pago ya está asignado.");
        }

        StripePaymentIntentId = OrderText.RequireToken(paymentIntentId, StripeIdMaxLength, "El intento de pago");
    }

    internal PaymentEvent RecordEvent(Guid eventId, string externalEventId, string eventType, DateTimeOffset receivedAt)
    {
        var key = OrderText.RequireToken(externalEventId, PaymentEvent.ExternalEventIdMaxLength, "El identificador del evento");
        if (events.Any(existing => existing.ExternalEventId == key))
        {
            throw new InvalidOperationException("El evento de pago ya está registrado.");
        }

        var paymentEvent = new PaymentEvent(this, eventId, key, eventType, receivedAt);
        events.Add(paymentEvent);
        return paymentEvent;
    }

    internal void MarkSucceeded(DateTimeOffset at)
    {
        EnsurePending();
        Status = PaymentStatus.Succeeded;
        SucceededAt = at;
    }

    internal void MarkFailed(DateTimeOffset at)
    {
        EnsurePending();
        Status = PaymentStatus.Failed;
        FailedAt = at;
    }

    internal void MarkCancelled(DateTimeOffset at)
    {
        EnsurePending();
        Status = PaymentStatus.Cancelled;
        CancelledAt = at;
    }

    internal void MarkRefunded(DateTimeOffset at)
    {
        if (Status != PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException("Solo se puede registrar el reembolso de un pago cobrado.");
        }

        Status = PaymentStatus.Refunded;
        RefundedAt = at;
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("El pago ya no está pendiente.");
        }
    }
}
