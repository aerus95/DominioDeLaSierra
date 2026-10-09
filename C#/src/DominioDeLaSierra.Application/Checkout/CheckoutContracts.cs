using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public static class CheckoutLimits
{
    public const int MaxLines = 30;
    public const int MaxQuantityPerLine = 99;

    public const string TooManyLinesMessage = "El pedido no puede superar 30 productos.";
    public const string QuantityMessage = "La cantidad no es válida.";
    public const string TooMuchQuantityMessage = "La cantidad no puede superar 99 unidades.";
    public const string RepeatedProductMessage = "El producto está repetido en el pedido.";
    public const string EmptyOrderMessage = "El pedido debe incluir al menos un producto.";
    public const string IdempotencyMessage = "La clave de idempotencia no es válida.";
    public const string IdempotencyConflictMessage = "La clave de idempotencia ya se usó con otros datos.";
    public const string MissingBodyMessage = "El cuerpo de la petición es obligatorio.";
    public const string CheckoutAccessMessage = "El acceso al pago no es válido.";
    public const string PaymentUnavailableMessage = "No se ha podido iniciar el pago. El pedido sigue pendiente.";
    public const string HoldReleasedMessage = "No queda margen para un pago de 30 minutos. La reserva se ha liberado.";
    public const string PaidOrderMessage = "El pedido ya está pagado.";
    public const string CancelledOrderMessage = "El pedido está cancelado.";
}

public sealed record CheckoutLineRequest(Guid ProductId, int Quantity);

public sealed record CheckoutDestinationRequest(string? PostalCode, string? CountryCode);

public sealed record CheckoutQuoteRequest(
    IReadOnlyList<CheckoutLineRequest>? Lines,
    CheckoutDestinationRequest? Destination);

public sealed record PlaceGuestOrderRequest(
    IReadOnlyList<CheckoutLineRequest>? Lines,
    CheckoutDestinationRequest? Destination,
    string? CustomerName,
    string? Email,
    string? Phone,
    string? AddressLine,
    string? City,
    string? Province,
    string? DeliveryNotes);

public sealed record CheckoutQuoteLineDto(
    Guid ProductId,
    string Name,
    string Reference,
    string Kind,
    int Quantity,
    long UnitPriceCents,
    decimal VatRate,
    long LineTotalCents,
    long TaxableBaseCents,
    long VatCents);

/// <summary>
/// Presupuesto calculado en el servidor. El envío es un bruto de 8,00 € o 0 €.
/// ShippingVatRate queda null: el tipo del envío no está confirmado y no se aplica un 21 %.
/// </summary>
public sealed record CheckoutQuoteDto(
    IReadOnlyList<CheckoutQuoteLineDto> Lines,
    long ProductSubtotalCents,
    long ProductTaxableBaseCents,
    long ProductVatCents,
    long ShippingCents,
    decimal? ShippingVatRate,
    long TotalCents,
    string Currency,
    bool ShippingVatPending);

public sealed record GuestOrderDto(
    Guid OrderId,
    string Number,
    long ProductSubtotalCents,
    long ProductTaxableBaseCents,
    long ProductVatCents,
    long ShippingCents,
    decimal? ShippingVatRate,
    long TotalCents,
    string Currency,
    DateTimeOffset ReservationExpiresAt,
    string Status,
    bool ShippingVatPending,
    string? CheckoutAccessToken = null);

public sealed record GuestOrderPlacement(GuestOrderDto Order, bool Created);

public sealed record StartCheckoutSessionRequest(Guid OrderId);

public sealed record CheckoutSessionDto(string CheckoutUrl, DateTimeOffset ExpiresAt);

public sealed record CheckoutPaymentDto(
    Guid OrderId,
    string Number,
    long ProductSubtotalCents,
    long ShippingCents,
    decimal? ShippingVatRate,
    long TotalCents,
    string Currency,
    DateTimeOffset ReservationExpiresAt,
    string Status,
    bool ShippingVatPending,
    string CheckoutUrl,
    DateTimeOffset ExpiresAt,
    string? CheckoutAccessToken = null);

public sealed record StripeCheckoutDraft(
    Guid OrderId,
    string OrderNumber,
    long AmountCents,
    IReadOnlyList<StripeCheckoutLines.Line> Lines,
    DateTimeOffset ExpiresAt,
    string SuccessUrl,
    string CancelUrl);

public sealed record CreatedStripeCheckout(string SessionId, string Url, long AmountCents, DateTimeOffset ExpiresAt);

public interface IStripeCheckoutGateway
{
    Task<CreatedStripeCheckout> CreateAsync(
        StripeCheckoutDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<StripeSessionInspection> InspectAsync(
        StripeSessionQuery query,
        CancellationToken cancellationToken = default);
}

public interface IOrderPaymentSessions
{
    Task<CheckoutPaymentDto> PayAsync(
        string? idempotencyKey,
        PlaceGuestOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<CheckoutSessionDto> StartAsync(
        Guid orderId,
        string? checkoutAccessToken,
        CancellationToken cancellationToken = default);
}

public interface ICheckout
{
    Task<CheckoutQuoteDto> QuoteAsync(CheckoutQuoteRequest request, CancellationToken cancellationToken = default);

    Task<GuestOrderPlacement> PlaceGuestOrderAsync(
        string? idempotencyKey,
        PlaceGuestOrderRequest request,
        CancellationToken cancellationToken = default,
        TimeSpan? reservationDuration = null);
}
