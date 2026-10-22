using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Payment
{
    public const int StripeIdMaxLength = 255;
    public const int ExternalIdMaxLength = 255;
    public const int ProviderMaxLength = 16;
    public const int RedsysOrderNumberMaxLength = RedsysOperationNumber.MaxLength;
    public const int CheckoutUrlMaxLength = 2048;
    public const int ExternalRequestIdMaxLength = 38;
    public const int ExternalRequestFingerprintMaxLength = 64;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public PaymentProvider Provider { get; private set; } = PaymentProvider.Stripe;
    public PaymentStatus Status { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = OrderAmounts.Currency;
    public string? ExternalOperationId { get; private set; }
    public string? ExternalCaptureId { get; private set; }
    public string? RedsysOrderNumber { get; private set; }
    public string? StripeCheckoutSessionId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTimeOffset? CheckoutExpiresAt { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string? ExternalRequestId { get; private set; }
    public string? ExternalRequestFingerprint { get; private set; }
    public DateTimeOffset? ExternalCreateAttemptedAt { get; private set; }
    public PayPalCreateOutcome? ExternalCreateOutcome { get; private set; }

    public Order Order { get; private set; } = null!;
    public IReadOnlyList<PaymentEvent> Events => events;

    private readonly List<PaymentEvent> events = [];

    private Payment()
    {
    }

    internal Payment(Guid id, Order order, long amountCents, DateTimeOffset createdAt, PaymentProvider provider)
    {
        OrderText.RequireId(id, "El pago");
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentException(PaymentProviderRules.UnknownMessage);
        }

        if (amountCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents), "El importe del pago no es válido.");
        }

        Id = id;
        Order = order;
        OrderId = order.Id;
        Provider = provider;
        Status = PaymentStatus.Pending;
        AmountCents = amountCents;
        Currency = OrderAmounts.Currency;
        CreatedAt = createdAt;
    }

    public string? ReconciliationReference()
    {
        return Provider == PaymentProvider.Stripe
            ? StripeCheckoutSessionId ?? ExternalOperationId
            : ExternalOperationId;
    }

    internal void AssignCheckoutSession(string sessionId)
    {
        EnsureStripe();
        EnsurePending();
        if (StripeCheckoutSessionId is not null)
        {
            throw new InvalidOperationException("La sesión de pago ya está asignada.");
        }

        var session = OrderText.RequireToken(sessionId, StripeIdMaxLength, "La sesión de pago");
        StripeCheckoutSessionId = session;
        RememberExternalOperation(session);
    }

    internal void AssignHostedCheckout(string sessionId, string checkoutUrl, DateTimeOffset expiresAt)
    {
        EnsureStripe();
        EnsurePending();
        if (StripeCheckoutSessionId is not null)
        {
            throw new InvalidOperationException("La sesión de pago ya está asignada.");
        }

        var session = OrderText.RequireToken(sessionId, StripeIdMaxLength, "La sesión de pago");
        if (!session.StartsWith("cs_test_", StringComparison.Ordinal)
            || session.Contains("cs_live_", StringComparison.OrdinalIgnoreCase))
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
        RememberExternalOperation(session);
    }

    internal void AssignExternalCheckout(
        string externalOperationId,
        string checkoutUrl,
        DateTimeOffset expiresAt,
        string? redsysOrderNumber)
    {
        EnsurePending();
        if (Provider is not (PaymentProvider.Redsys or PaymentProvider.PayPal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalProviderMessage);
        }

        if (StripeCheckoutSessionId is not null || StripePaymentIntentId is not null)
        {
            throw new InvalidOperationException(PaymentProviderRules.StripeReferenceMessage);
        }

        if (ExternalOperationId is not null)
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }

        var operation = RequireGatewayReference(externalOperationId, "La referencia externa");
        var url = OrderText.RequireToken(checkoutUrl, CheckoutUrlMaxLength, "La dirección de pago");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || string.Equals(parsed.Host, "checkout.stripe.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La dirección de pago no es válida.");
        }

        if (expiresAt <= CreatedAt)
        {
            throw new ArgumentException("La caducidad del pago no es válida.");
        }

        string? number = null;
        if (Provider == PaymentProvider.Redsys)
        {
            number = RedsysOperationNumber.Require(redsysOrderNumber);
        }
        else if (!string.IsNullOrWhiteSpace(redsysOrderNumber))
        {
            throw new ArgumentException(PaymentProviderRules.RedsysMismatchMessage);
        }

        ExternalOperationId = operation;
        RedsysOrderNumber = number;
        CheckoutUrl = url;
        CheckoutExpiresAt = expiresAt;
    }

    public bool PayPalHoldMustStay()
    {
        if (Provider != PaymentProvider.PayPal || Status != PaymentStatus.Pending)
        {
            return false;
        }

        if (ExternalCreateOutcome is PayPalCreateOutcome.Attempted or PayPalCreateOutcome.Uncertain)
        {
            return true;
        }

        if (ExternalOperationId is not null && CheckoutUrl is null)
        {
            return true;
        }

        return ExternalOperationId is null && HasCreateEvidence();
    }

    public bool BlocksAutomaticRelease()
    {
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunded)
        {
            return false;
        }

        if (ExternalCaptureId is not null && Status == PaymentStatus.Pending)
        {
            return true;
        }

        if (Provider != PaymentProvider.PayPal || Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            return false;
        }

        if (ExternalCreateOutcome is PayPalCreateOutcome.Attempted or PayPalCreateOutcome.Uncertain)
        {
            return true;
        }

        if (ExternalOperationId is not null
            && CheckoutUrl is null
            && ExternalCreateOutcome != PayPalCreateOutcome.Identified)
        {
            return true;
        }

        return ExternalOperationId is null && HasCreateEvidence();
    }

    internal void AcceptVerifiedUnpaidClosure()
    {
        if (Provider != PaymentProvider.PayPal || string.IsNullOrWhiteSpace(ExternalOperationId))
        {
            throw new InvalidOperationException(ExternalHoldRules.UncertainHoldMessage);
        }

        if (ExternalCreateOutcome is null or PayPalCreateOutcome.Attempted or PayPalCreateOutcome.Uncertain)
        {
            ExternalCreateOutcome = PayPalCreateOutcome.Identified;
        }
    }

    private bool HasCreateEvidence()
    {
        return events.Any(item =>
            item.ExternalEventId == PayPalCreateMarkers.EventId(Id)
            || string.Equals(item.EventType, "paypal.order.create", StringComparison.Ordinal));
    }

    public PayPalCreateBlock CreateBlock(DateTimeOffset now)
    {
        if (Provider != PaymentProvider.PayPal || Status != PaymentStatus.Pending || ExternalOperationId is not null)
        {
            return PayPalCreateBlock.None;
        }

        if (!PayPalHoldMustStay())
        {
            return PayPalCreateBlock.None;
        }

        var started = ExternalCreateAttemptedAt
            ?? events
                .Where(item => item.ExternalEventId == PayPalCreateMarkers.EventId(Id))
                .Select(item => (DateTimeOffset?)item.ReceivedAt)
                .FirstOrDefault();
        if (started is DateTimeOffset attemptedAt && !PayPalCreateMarkers.WithinDocumentedWindow(attemptedAt, now))
        {
            return PayPalCreateBlock.OutsideDocumentedWindow;
        }

        return PayPalCreateBlock.InsideDocumentedWindow;
    }

    internal void BeginPayPalCreate(string requestId, string fingerprint, DateTimeOffset at)
    {
        if (Provider != PaymentProvider.PayPal)
        {
            throw new InvalidOperationException(PaymentProviderRules.NotPayPalMessage);
        }

        EnsurePending();
        if (ExternalCreateOutcome is PayPalCreateOutcome.Uncertain or PayPalCreateOutcome.Identified || ExternalOperationId is not null)
        {
            throw new InvalidOperationException("La creación de PayPal ya no puede repetirse.");
        }

        var key = OrderText.RequireToken(requestId, ExternalRequestIdMaxLength, "La clave de PayPal");
        var print = OrderText.RequireToken(fingerprint, ExternalRequestFingerprintMaxLength, "La huella de PayPal");
        if (print.Length != ExternalRequestFingerprintMaxLength || print.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("La huella de PayPal no es válida.");
        }

        if (ExternalRequestId is null)
        {
            ExternalRequestId = key;
            ExternalRequestFingerprint = print.ToLowerInvariant();
            ExternalCreateAttemptedAt = at;
            ExternalCreateOutcome = PayPalCreateOutcome.Attempted;
            return;
        }

        if (!string.Equals(ExternalRequestId, key, StringComparison.Ordinal)
            || !string.Equals(ExternalRequestFingerprint, print, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }
    }

    internal void RememberPayPalOrder(string externalOperationId)
    {
        if (Provider != PaymentProvider.PayPal)
        {
            throw new InvalidOperationException(PaymentProviderRules.NotPayPalMessage);
        }

        EnsurePending();
        var operation = RequireGatewayReference(externalOperationId, "La referencia externa");
        if (ExternalOperationId is null)
        {
            ExternalOperationId = operation;
        }
        else if (!string.Equals(ExternalOperationId, operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }

        ExternalCreateOutcome = PayPalCreateOutcome.Identified;
    }

    internal void NotePayPalCreateUncertain()
    {
        if (Provider != PaymentProvider.PayPal || ExternalOperationId is not null)
        {
            return;
        }

        if (ExternalCreateOutcome != PayPalCreateOutcome.Attempted)
        {
            return;
        }

        ExternalCreateOutcome = PayPalCreateOutcome.Uncertain;
    }

    internal void AttachPayPalApproval(string externalOperationId, string checkoutUrl, DateTimeOffset expiresAt)
    {
        if (Provider != PaymentProvider.PayPal)
        {
            throw new InvalidOperationException(PaymentProviderRules.NotPayPalMessage);
        }

        EnsurePending();
        if (ExternalOperationId is null
            || !string.Equals(ExternalOperationId, externalOperationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }

        var url = OrderText.RequireToken(checkoutUrl, CheckoutUrlMaxLength, "La dirección de pago");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.Equals(parsed.Host, "www.sandbox.paypal.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La dirección de pago no es válida.");
        }

        if (expiresAt <= CreatedAt)
        {
            throw new ArgumentException("La caducidad del pago no es válida.");
        }

        if (CheckoutUrl is null)
        {
            CheckoutUrl = url;
            CheckoutExpiresAt = expiresAt;
            ExternalCreateOutcome = PayPalCreateOutcome.Identified;
            return;
        }

        if (!string.Equals(CheckoutUrl, url, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }
    }

    internal void AssignCapture(string captureId)
    {
        if (Provider != PaymentProvider.PayPal)
        {
            throw new InvalidOperationException(PaymentProviderRules.NotPayPalMessage);
        }

        EnsurePending();
        var capture = RequireGatewayReference(captureId, "La captura");
        if (ExternalCaptureId is null)
        {
            ExternalCaptureId = capture;
            return;
        }

        if (!string.Equals(ExternalCaptureId, capture, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }
    }

    internal void AssignPaymentIntent(string paymentIntentId)
    {
        EnsureStripe();
        EnsurePending();
        if (StripePaymentIntentId is not null)
        {
            throw new InvalidOperationException("El intento de pago ya está asignado.");
        }

        var intent = OrderText.RequireToken(paymentIntentId, StripeIdMaxLength, "El intento de pago");
        StripePaymentIntentId = intent;
        if (ExternalCaptureId is null)
        {
            ExternalCaptureId = intent;
            return;
        }

        if (!string.Equals(ExternalCaptureId, intent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }
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

    private void EnsureStripe()
    {
        if (Provider != PaymentProvider.Stripe)
        {
            throw new InvalidOperationException(PaymentProviderRules.NotStripeMessage);
        }
    }

    private void RememberExternalOperation(string operationId)
    {
        if (ExternalOperationId is null)
        {
            ExternalOperationId = operationId;
            return;
        }

        if (!string.Equals(ExternalOperationId, operationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PaymentProviderRules.ExternalAssignedMessage);
        }
    }

    private static string RequireGatewayReference(string? value, string label)
    {
        var text = OrderText.RequireToken(value, ExternalIdMaxLength, label);
        if (text.StartsWith("cs_", StringComparison.Ordinal) || text.StartsWith("pi_", StringComparison.Ordinal))
        {
            throw new ArgumentException(PaymentProviderRules.ExternalMismatchMessage);
        }

        if (text.Length is >= 13 and <= 19 && text.All(char.IsDigit))
        {
            throw new ArgumentException(PaymentProviderRules.CardMessage);
        }

        return text;
    }
}
