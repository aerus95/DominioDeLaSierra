using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Doble de pruebas local. No llama a api-m.sandbox.paypal.com y no demuestra un cobro real de Sandbox.
/// Solo está cableado cuando el entorno es Testing y PayPal:Gateway es Script.
/// </summary>
public sealed class PayPalOrderScript(IConfiguration configuration) : IPayPalOrders, IPayPalCertificates
{
    private readonly object gate = new();
    private readonly Dictionary<string, PayPalOrderSnapshot> created = new(StringComparer.Ordinal);
    private long amountCents;
    private string? customId;
    private string? invoiceId;

    public string PaypalOrderId { get; set; } = "5O190127TN364715T";

    public string CaptureId { get; set; } = "3C679366HH908993F";

    public string Status { get; set; } = "CREATED";

    public string CreateStatus { get; set; } = "CREATED";

    public string? ApproveUrlOverride { get; set; }

    public string NextCaptureStatus { get; set; } = "COMPLETED";

    public bool IncludeCapture { get; set; }

    public long? AmountOverride { get; set; }

    public string? CurrencyOverride { get; set; }

    public bool ThrowOnCapture { get; set; }

    public bool AlreadyCaptured { get; set; }

    public string? CertificatePem { get; set; }

    public bool ThrowOnGet { get; set; }

    public bool LoseCreateResponse { get; set; }

    public int CreateCalls { get; private set; }

    public int GetCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public List<string> CreateRequestIds { get; } = [];

    public List<string> CaptureRequestIds { get; } = [];

    public Task<PayPalOrderSnapshot> CreateAsync(
        PayPalCreateCommand command,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            CreateCalls++;
            CreateRequestIds.Add(requestId);
            if (created.TryGetValue(requestId, out var existing))
            {
                return Task.FromResult(existing);
            }

            amountCents = command.AmountCents;
            customId = PayPalProtocol.CustomId(command.OrderId);
            invoiceId = command.OrderNumber;
            Status = CreateStatus;
            IncludeCapture = false;
            var snapshot = Current();
            created[requestId] = snapshot;
            if (LoseCreateResponse)
            {
                throw new PayPalTransportException();
            }

            return Task.FromResult(snapshot);
        }
    }

    public Task<PayPalOrderSnapshot> GetAsync(string paypalOrderId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            GetCalls++;
            if (ThrowOnGet || !string.Equals(paypalOrderId, PaypalOrderId, StringComparison.Ordinal))
            {
                throw new PayPalTransportException();
            }

            return Task.FromResult(Current());
        }
    }

    public Task<PayPalOrderSnapshot> CaptureAsync(
        string paypalOrderId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            CaptureCalls++;
            CaptureRequestIds.Add(requestId);
            if (!string.Equals(paypalOrderId, PaypalOrderId, StringComparison.Ordinal))
            {
                throw new PayPalTransportException();
            }

            if (ThrowOnCapture)
            {
                throw new PayPalTransportException();
            }

            if (AlreadyCaptured)
            {
                throw new PayPalAlreadyCapturedException();
            }

            if (CaptureRequestIds.Count(item => item == requestId) > 1 && Status == "COMPLETED")
            {
                return Task.FromResult(Current());
            }

            Status = "COMPLETED";
            IncludeCapture = true;
            return Task.FromResult(Current());
        }
    }

    public Task<string> ReadPemAsync(string certificateUrl, CancellationToken cancellationToken = default)
    {
        if (!PayPalProtocol.IsSandboxCertificateUrl(certificateUrl) || string.IsNullOrWhiteSpace(CertificatePem))
        {
            throw new PayPalSignatureException();
        }

        return Task.FromResult(CertificatePem);
    }

    public string WebhookId()
    {
        return configuration["PayPal:WebhookId"]?.Trim() ?? string.Empty;
    }

    private PayPalOrderSnapshot Current()
    {
        var cents = AmountOverride ?? amountCents;
        var currency = CurrencyOverride ?? PayPalRules.Currency;
        var captures = IncludeCapture
            ? new[] { new PayPalCaptureView(CaptureId, NextCaptureStatus, cents, currency) }
            : Array.Empty<PayPalCaptureView>();
        return new PayPalOrderSnapshot(
            PaypalOrderId,
            Status,
            customId,
            invoiceId,
            cents,
            currency,
            ApproveUrlOverride ?? $"https://www.sandbox.paypal.com/checkoutnow?token={PaypalOrderId}",
            captures,
            customId is null ? 0 : 1);
    }
}
