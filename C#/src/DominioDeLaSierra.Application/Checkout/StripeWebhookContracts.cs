namespace DominioDeLaSierra.Application.Checkout;

public static class StripeWebhookRules
{
    public const int MaxBodyBytes = 128 * 1024;

    public const string AmountMessage = "El importe cobrado no coincide con el pedido.";
    public const string CurrencyMessage = "La moneda cobrada no es EUR.";
    public const string StockMessage = "El cobro no tiene stock reservado. Hace falta reconciliar o reembolsar.";
    public const string ReferenceMessage = "La sesión no coincide con el pedido guardado.";
    public const string IntentMessage = "El intento de pago no es de pruebas.";
    public const string ClosedOrderMessage = "El pedido ya no admite el cobro. Hace falta reconciliar o reembolsar.";
    public const string UnavailableMessage = "No se ha podido procesar el webhook.";

    public const string Confirmed = "confirmed";
    public const string Duplicate = "duplicate";
    public const string Ignored = "ignored";
    public const string NeedsReconciliation = "needs-reconciliation";
}

public sealed record StripeWebhookNotice(
    string EventId,
    string EventType,
    bool LiveMode,
    string? SessionId,
    string? PaymentStatus,
    long? AmountTotal,
    string? Currency,
    string? PaymentIntentId,
    string? ClientReferenceId,
    string? MetadataOrderId);

public sealed record StripeWebhookResult(string Disposition);

public interface IStripeWebhookParser
{
    StripeWebhookNotice Parse(string payload, string? signatureHeader);
}

public interface IStripeWebhooks
{
    Task<StripeWebhookResult> ReceiveAsync(
        string? payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}
