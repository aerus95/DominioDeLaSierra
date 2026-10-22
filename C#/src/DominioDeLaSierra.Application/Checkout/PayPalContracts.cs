using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public sealed record PayPalCreateCommand(
    Guid OrderId,
    string OrderNumber,
    long AmountCents,
    string FullName,
    string AddressLine,
    string PostalCode,
    string City,
    string Province,
    string ReturnUrl,
    string CancelUrl);

public sealed record PayPalCaptureView(string Id, string Status, long? AmountCents, string? Currency);

/// <summary>
/// Recurso de orden de Orders v2 ya leído. No incluye datos de pago del comprador.
/// </summary>
public sealed record PayPalOrderSnapshot(
    string Id,
    string Status,
    string? CustomId,
    string? InvoiceId,
    long? AmountCents,
    string? Currency,
    string? ApproveUrl,
    IReadOnlyList<PayPalCaptureView> Captures,
    int PurchaseUnitCount);

public sealed record PayPalReading(PaymentEvidence Evidence, string? Attention, string? CaptureId, bool CanCapture);

public sealed record PayPalTransmission(
    string? TransmissionId,
    string? TransmissionTime,
    string? Signature,
    string? CertificateUrl,
    string? Algorithm);

public sealed record PayPalEventNotice(string EventId, string EventType, string PaypalOrderId, Guid? LocalOrderId = null);

public sealed record PayPalCaptureRequest(Guid OrderId);

public interface IPayPalOrders
{
    Task<PayPalOrderSnapshot> CreateAsync(
        PayPalCreateCommand command,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderSnapshot> GetAsync(string paypalOrderId, CancellationToken cancellationToken = default);

    Task<PayPalOrderSnapshot> CaptureAsync(
        string paypalOrderId,
        string requestId,
        CancellationToken cancellationToken = default);
}

public interface IPayPalCertificates
{
    Task<string> ReadPemAsync(string certificateUrl, CancellationToken cancellationToken = default);
}

public interface IPayPalCaptures
{
    Task<CheckoutOutcomeDto> CaptureAsync(
        Guid orderId,
        string? checkoutAccessToken,
        CancellationToken cancellationToken = default);
}

public interface IPayPalWebhooks
{
    Task ReceiveAsync(
        PayPalTransmission transmission,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lectura de una orden ya creada. No captura y no usa la URL de retorno del navegador.
/// </summary>
public sealed record PayPalObservation(PaymentEvidence Evidence, string? Attention, bool StockChanged);

public interface IPayPalObservations
{
    Task<PayPalObservation> ObserveAsync(
        Guid orderId,
        string auditEventId,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalTransportException : Exception
{
    public PayPalTransportException()
        : base(PayPalRules.TransportMessage)
    {
    }
}

public sealed class PayPalSignatureException : Exception
{
    public PayPalSignatureException()
        : base(PayPalRules.SignatureMessage)
    {
    }
}

public sealed class PayPalAlreadyCapturedException : Exception
{
    public PayPalAlreadyCapturedException()
        : base(PayPalRules.AlreadyCapturedMessage)
    {
    }
}
