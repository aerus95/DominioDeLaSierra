using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Order
{
    public const int NumberMaxLength = 40;
    public const int IdempotencyKeyMaxLength = 255;
    public const int CustomerNameMaxLength = 150;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int AddressLineMaxLength = 200;
    public const int PostalCodeMaxLength = 10;
    public const int CityMaxLength = 120;
    public const int ProvinceMaxLength = 120;
    public const int DeliveryNotesMaxLength = 500;
    public const int CarrierMaxLength = 80;
    public const int TrackingNumberMaxLength = 80;

    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string AddressLine { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string Province { get; private set; } = null!;
    public string CountryCode { get; private set; } = OrderAmounts.CountryCode;
    public string? DeliveryNotes { get; private set; }
    public long ProductSubtotalCents { get; private set; }
    public long ProductTaxableBaseCents { get; private set; }
    public long ProductVatCents { get; private set; }
    public long ShippingCents { get; private set; }
    public decimal? ShippingVatRate { get; private set; }
    public long? ShippingTaxableBaseCents { get; private set; }
    public long? ShippingVatCents { get; private set; }
    public long TotalCents { get; private set; }
    public string Currency { get; private set; } = OrderAmounts.Currency;
    public string IdempotencyKey { get; private set; } = null!;
    public string CheckoutAccessTokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ReservationExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public DateTimeOffset? FulfillmentUpdatedAt { get; private set; }
    public Guid? FulfillmentUpdatedByUserId { get; private set; }

    public IReadOnlyList<OrderItem> Items => items;
    public IReadOnlyList<Payment> Payments => payments;
    public IReadOnlyList<FulfillmentTransition> FulfillmentTransitions => fulfillmentTransitions;

    private readonly List<OrderItem> items = [];
    private readonly List<Payment> payments = [];
    private readonly List<FulfillmentTransition> fulfillmentTransitions = [];

    private Order()
    {
    }

    public static Order CreatePending(
        Guid id,
        string number,
        string idempotencyKey,
        string customerName,
        string email,
        string phone,
        string addressLine,
        string postalCode,
        string city,
        string province,
        string? deliveryNotes,
        IReadOnlyList<OrderLine> lines,
        DateTimeOffset createdAt,
        string? checkoutAccessTokenHash = null,
        TimeSpan? reservationDuration = null)
    {
        OrderText.RequireId(id, "El pedido");
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("El pedido debe incluir al menos un producto.");
        }

        var duration = reservationDuration ?? OrderAmounts.ReservationDuration;
        if (duration <= TimeSpan.Zero || duration > StripeCheckoutExpiry.Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(reservationDuration), "La duración de la reserva no es válida.");
        }

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = createdAt.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(createdAt), "La fecha del pedido no es válida.");
        }

        var order = new Order
        {
            Id = id,
            Number = OrderText.RequireToken(number, NumberMaxLength, "El número de pedido"),
            Status = OrderStatus.PendingPayment,
            CustomerName = OrderText.Require(customerName, CustomerNameMaxLength, "El nombre"),
            Email = OrderText.RequireEmail(email),
            Phone = OrderText.Require(phone, PhoneMaxLength, "El teléfono"),
            AddressLine = OrderText.Require(addressLine, AddressLineMaxLength, "La dirección"),
            PostalCode = OrderText.Require(postalCode, PostalCodeMaxLength, "El código postal"),
            City = OrderText.Require(city, CityMaxLength, "La localidad"),
            Province = OrderText.Require(province, ProvinceMaxLength, "La provincia"),
            CountryCode = OrderAmounts.CountryCode,
            DeliveryNotes = OrderText.Optional(deliveryNotes, DeliveryNotesMaxLength, "Las indicaciones de entrega"),
            Currency = OrderAmounts.Currency,
            IdempotencyKey = OrderText.RequireToken(idempotencyKey, IdempotencyKeyMaxLength, "La clave de idempotencia"),
            CheckoutAccessTokenHash = CheckoutAccessToken.RequireHash(
                checkoutAccessTokenHash ?? CheckoutAccessToken.Hash(CheckoutAccessToken.Create())),
            CreatedAt = createdAt,
            ReservationExpiresAt = expiresAt,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled
        };

        var seenProducts = new HashSet<Guid>();
        long subtotal = 0;
        long taxableBase = 0;
        long vat = 0;
        try
        {
            foreach (var line in lines)
            {
                if (!seenProducts.Add(line.ProductId))
                {
                    throw new ArgumentException("El producto está repetido en el pedido.");
                }

                var item = OrderItem.Create(order, line);
                order.items.Add(item);
                subtotal = checked(subtotal + item.LineTotalCents);
                taxableBase = checked(taxableBase + item.TaxableBaseCents);
                vat = checked(vat + item.VatCents);
            }

            var shipping = OrderAmounts.ShippingCentsForProductSubtotal(subtotal);
            order.ProductSubtotalCents = subtotal;
            order.ProductTaxableBaseCents = taxableBase;
            order.ProductVatCents = vat;
            order.ShippingCents = shipping;
            order.TotalCents = checked(subtotal + shipping);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), "El importe del pedido no es válido.");
        }

        return order;
    }

    public void DefineShippingVat(decimal vatRate)
    {
        EnsurePending();
        if (payments.Count > 0)
        {
            throw new InvalidOperationException("El IVA del envío debe definirse antes de iniciar el pago.");
        }

        if (ShippingVatRate is not null)
        {
            throw new InvalidOperationException("El IVA del envío ya está definido.");
        }

        var (taxableBase, vat) = OrderAmounts.SplitGross(ShippingCents, vatRate);
        ShippingVatRate = vatRate;
        ShippingTaxableBaseCents = taxableBase;
        ShippingVatCents = vat;
    }

    public Payment AddPayment(Guid paymentId, DateTimeOffset createdAt)
    {
        EnsurePending();
        if (payments.Any(payment => payment.Status == PaymentStatus.Pending))
        {
            throw new InvalidOperationException("El pedido ya tiene un pago pendiente.");
        }

        var payment = new Payment(paymentId, this, TotalCents, createdAt);
        payments.Add(payment);
        return payment;
    }

    public void AssignCheckoutSession(Guid paymentId, string sessionId)
    {
        EnsurePending();
        RequirePayment(paymentId).AssignCheckoutSession(sessionId);
    }

    public void AssignHostedCheckout(Guid paymentId, string sessionId, string checkoutUrl, DateTimeOffset expiresAt)
    {
        EnsurePending();
        RequirePayment(paymentId).AssignHostedCheckout(sessionId, checkoutUrl, expiresAt);
    }

    public void AssignPaymentIntent(Guid paymentId, string paymentIntentId)
    {
        EnsurePending();
        RequirePayment(paymentId).AssignPaymentIntent(paymentIntentId);
    }

    public PaymentEvent RecordPaymentEvent(
        Guid paymentId,
        Guid eventId,
        string externalEventId,
        string eventType,
        DateTimeOffset receivedAt)
    {
        return RequirePayment(paymentId).RecordEvent(eventId, externalEventId, eventType, receivedAt);
    }

    public void MarkPaymentEventProcessed(Guid paymentId, string externalEventId, DateTimeOffset at, string? attentionReason = null)
    {
        var key = OrderText.RequireToken(externalEventId, PaymentEvent.ExternalEventIdMaxLength, "El identificador del evento");
        var paymentEvent = RequirePayment(paymentId).Events.SingleOrDefault(item => item.ExternalEventId == key)
            ?? throw new InvalidOperationException("El evento de pago no existe.");
        paymentEvent.MarkProcessed(at, attentionReason);
    }

    public void MarkPaid(Guid paymentId, DateTimeOffset at)
    {
        EnsurePending();
        RequirePayment(paymentId).MarkSucceeded(at);
        Status = OrderStatus.Paid;
        PaidAt = at;
    }

    public void RecordPaymentFailure(Guid paymentId, DateTimeOffset at)
    {
        EnsurePending();
        RequirePayment(paymentId).MarkFailed(at);
    }

    public void RecordPaymentCancellation(Guid paymentId, DateTimeOffset at)
    {
        EnsurePending();
        RequirePayment(paymentId).MarkCancelled(at);
    }

    public void RecordRefund(Guid paymentId, DateTimeOffset at)
    {
        if (Status != OrderStatus.Paid)
        {
            throw new InvalidOperationException("Solo se puede registrar un reembolso de un pedido pagado.");
        }

        RequirePayment(paymentId).MarkRefunded(at);
    }

    public void Cancel(DateTimeOffset at)
    {
        EnsurePending();
        CancelPendingPayments(at);
        Status = OrderStatus.Cancelled;
        CancelledAt = at;
    }

    public void AdvanceFulfillment(
        FulfillmentStatus target,
        DateTimeOffset at,
        Guid actorUserId,
        string? carrier,
        string? trackingNumber)
    {
        if (Status != OrderStatus.Paid)
        {
            throw new InvalidOperationException("Solo se puede preparar un pedido pagado.");
        }

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentException("El estado de preparación no es válido.");
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("El usuario responsable no es válido.");
        }

        if (at < CreatedAt)
        {
            throw new ArgumentException("La fecha del cambio no es válida.");
        }

        if (target == FulfillmentStatus)
        {
            throw new InvalidOperationException("El pedido ya está en ese estado de preparación.");
        }

        if (target != NextFulfillment(FulfillmentStatus))
        {
            throw new InvalidOperationException("El pedido no puede pasar a ese estado de preparación.");
        }

        var nextCarrier = OrderText.Optional(carrier, CarrierMaxLength, "El transportista") ?? Carrier;
        var nextTracking = OrderText.Optional(trackingNumber, TrackingNumberMaxLength, "El número de seguimiento") ?? TrackingNumber;
        var from = FulfillmentStatus;
        fulfillmentTransitions.Add(new FulfillmentTransition(
            Guid.NewGuid(),
            this,
            from,
            target,
            at,
            actorUserId,
            nextCarrier,
            nextTracking));
        FulfillmentStatus = target;
        Carrier = nextCarrier;
        TrackingNumber = nextTracking;
        FulfillmentUpdatedAt = at;
        FulfillmentUpdatedByUserId = actorUserId;
    }

    public void Expire(DateTimeOffset at)
    {
        EnsurePending();
        if (at < ReservationExpiresAt)
        {
            throw new InvalidOperationException("La reserva todavía no ha caducado.");
        }

        CancelPendingPayments(at);
        Status = OrderStatus.Expired;
        ExpiredAt = at;
    }

    private void CancelPendingPayments(DateTimeOffset at)
    {
        foreach (var payment in payments)
        {
            if (payment.Status == PaymentStatus.Pending)
            {
                payment.MarkCancelled(at);
            }
        }
    }

    private Payment RequirePayment(Guid paymentId)
    {
        return payments.SingleOrDefault(payment => payment.Id == paymentId)
            ?? throw new InvalidOperationException("El pago no pertenece al pedido.");
    }

    private static FulfillmentStatus? NextFulfillment(FulfillmentStatus current)
    {
        return current switch
        {
            FulfillmentStatus.Unfulfilled => FulfillmentStatus.Preparing,
            FulfillmentStatus.Preparing => FulfillmentStatus.Prepared,
            FulfillmentStatus.Prepared => FulfillmentStatus.Shipped,
            _ => null
        };
    }

    private void EnsurePending()
    {
        if (Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException("El pedido no está pendiente de pago.");
        }
    }
}
