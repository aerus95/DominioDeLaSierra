using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Admin;

public sealed record AdminOrderListQuery(
    string? Number,
    string? Customer,
    OrderStatus? Status,
    FulfillmentStatus? Fulfillment,
    PaymentStatus? Payment,
    DateOnly? From,
    DateOnly? To,
    int Page)
{
    public const int PageSize = 20;
    public const int MaxPage = 10_000;

    public bool InvertedDates => From is { } from && To is { } to && from > to;

    public static AdminOrderListQuery Create(
        string? number,
        string? customer,
        string? status,
        string? fulfillment,
        string? payment,
        string? from,
        string? to,
        int page)
    {
        return new AdminOrderListQuery(
            Trim(number, OrderTextLimit.Number),
            Trim(customer, OrderTextLimit.Customer),
            Parse<OrderStatus>(status),
            Parse<FulfillmentStatus>(fulfillment),
            Parse<PaymentStatus>(payment),
            ParseDate(from),
            ParseDate(to),
            page < 1 ? 1 : Math.Min(page, MaxPage));
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) ? date : null;
    }
}

internal static class OrderTextLimit
{
    public const int Number = 40;
    public const int Customer = 150;
}

public sealed record AdminOrderListItem(
    Guid Id,
    string Number,
    DateTimeOffset CreatedAt,
    string CustomerName,
    long TotalCents,
    string Currency,
    OrderStatus Status,
    FulfillmentStatus FulfillmentStatus,
    PaymentStatus? PaymentStatus);

public sealed record AdminOrderListPage(
    IReadOnlyList<AdminOrderListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int PageCount => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record AdminOrderComponent(
    string Name,
    string Reference,
    int QuantityPerPack);

public sealed record AdminOrderLine(
    string Name,
    string Reference,
    ProductKind Kind,
    int Quantity,
    long UnitPriceCents,
    long LineTotalCents,
    IReadOnlyList<AdminOrderComponent> Components);

public sealed record AdminPaymentIncident(
    string EventType,
    DateTimeOffset ReceivedAt,
    string AttentionReason);

public sealed record AdminOrderPayment(
    PaymentStatus Status,
    long AmountCents,
    string Currency,
    string? CheckoutSessionId,
    string? PaymentIntentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SucceededAt,
    IReadOnlyList<AdminPaymentIncident> Incidents);

public sealed record AdminOrderReservation(
    StockReservationStatus Status,
    DateTimeOffset ReservedAt,
    DateTimeOffset? ConfirmationStartedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ReleasedAt);

public sealed record AdminFulfillmentChange(
    FulfillmentStatus FromStatus,
    FulfillmentStatus ToStatus,
    DateTimeOffset OccurredAt,
    string ActorName,
    string? Carrier,
    string? TrackingNumber);

public sealed record AdminOrderDetail(
    Guid Id,
    string Number,
    DateTimeOffset CreatedAt,
    DateTimeOffset ReservationExpiresAt,
    OrderStatus Status,
    FulfillmentStatus FulfillmentStatus,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? FulfillmentUpdatedAt,
    string CustomerName,
    string Email,
    string Phone,
    string AddressLine,
    string PostalCode,
    string City,
    string Province,
    string CountryCode,
    string? DeliveryNotes,
    long ProductSubtotalCents,
    long ProductVatCents,
    long ShippingCents,
    decimal? ShippingVatRate,
    long? ShippingVatCents,
    long TotalCents,
    string Currency,
    IReadOnlyList<AdminOrderLine> Lines,
    IReadOnlyList<AdminOrderPayment> Payments,
    AdminOrderReservation? Reservation,
    IReadOnlyList<AdminFulfillmentChange> Changes);

public sealed record AdvanceFulfillment(
    Guid OrderId,
    FulfillmentStatus Target,
    Guid ActorUserId,
    string? Carrier,
    string? TrackingNumber);

public interface IAdminOrders
{
    Task<AdminOrderListPage> ListAsync(AdminOrderListQuery query, CancellationToken cancellationToken = default);

    Task<AdminOrderDetail?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task AdvanceFulfillmentAsync(AdvanceFulfillment command, CancellationToken cancellationToken = default);
}

public static class AdminOrderClock
{
    public static TimeZoneInfo Madrid { get; } = TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Madrid", out var madrid)
        ? madrid
        : TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    public static DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Madrid.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateTimeOffset Convert(DateTimeOffset value)
    {
        return TimeZoneInfo.ConvertTime(value, Madrid);
    }
}
