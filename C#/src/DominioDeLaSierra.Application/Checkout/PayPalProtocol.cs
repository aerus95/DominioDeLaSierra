using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

/// <summary>
/// Orders API v2 en Sandbox. La orden no trae expires_at: CheckoutExpiresAt del pago es la reserva local,
/// no un plazo publicado por PayPal y no los 34 minutos de Stripe.
/// APPROVED no está cobrado. COMPLETED obliga a leer el estado de la captura.
/// La anulación de Orders v2 no tiene endpoint de cancelación; VOIDED es el cierre documentado de las unidades.
/// </summary>
public static class PayPalRules
{
    public const string TransportMessage = "PayPal no ha respondido. El pedido sigue pendiente y el stock no se ha liberado.";
    public const string SignatureMessage = "La notificación de PayPal no es auténtica.";
    public const string AmountMessage = "El importe de PayPal no coincide con el pedido.";
    public const string CurrencyMessage = "La moneda de PayPal no es EUR.";
    public const string IdentityMessage = "La operación de PayPal no corresponde a este pedido.";
    public const string UnknownStatusMessage = "El estado de PayPal no permite decidir el stock.";
    public const string PendingCaptureMessage = "La captura de PayPal sigue pendiente.";
    public const string RefundMessage = "PayPal ha informado de un reembolso y ese flujo no está implementado.";
    public const string StockMessage = "El stock no está reservado para confirmar este cobro.";
    public const string AlreadyCapturedMessage = "PayPal ya había capturado la orden.";
    public const string ApprovalMessage = "PayPal no ha devuelto una aprobación de Sandbox.";
    public const string UncertainCreateMessage = "PayPal puede haber creado la orden y no conocemos su identificador. No se reintenta la creación y el stock sigue reservado.";
    public const string ExpiredIdempotencyMessage = "Ha pasado la ventana documentada de 6 horas de PayPal-Request-Id. No se crea otra orden y el stock sigue reservado.";
    public const string UncertainHoldMessage = "La reserva ha vencido, pero el cobro de PayPal sigue sin identificar. El stock no se libera.";
    public const int MaxWebhookBytes = 256 * 1024;
    public const int RequestIdMaxLength = 38;

    public const string SandboxApiRoot = "https://api-m.sandbox.paypal.com";
    public const string LiveApiRoot = "https://api-m.paypal.com";
    public const string Currency = "EUR";
    public const string IntentCapture = "CAPTURE";
    public const string Algorithm = "SHA256withRSA";
}

public static class PayPalProtocol
{
    private static readonly Regex OrderIdPattern = new("^[A-Z0-9]{10,36}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AmountPattern = new("^[0-9]+\\.[0-9]{2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool UseScript(string? environmentName, string? gateway)
    {
        return string.Equals(environmentName, "Testing", StringComparison.Ordinal)
            && string.Equals(gateway, "Script", StringComparison.Ordinal);
    }

    public static string CreateRequestId(Guid paymentId)
    {
        return Fit("o" + paymentId.ToString("N"));
    }

    public static string CaptureRequestId(Guid paymentId)
    {
        return Fit("c" + paymentId.ToString("N"));
    }

    public static string CustomId(Guid orderId)
    {
        return orderId.ToString("D");
    }

    public static string RequestFingerprint(PayPalCreateCommand command)
    {
        var canonical = string.Join(
            '\n',
            command.OrderId.ToString("D"),
            command.OrderNumber,
            command.AmountCents.ToString(CultureInfo.InvariantCulture),
            command.FullName,
            command.AddressLine,
            command.PostalCode,
            command.City,
            command.Province,
            command.ReturnUrl,
            command.CancelUrl);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// Un POST repetido no consulta la orden. Solo el primer intento, antes de cualquier llamada, puede crearla.
    /// Dentro o fuera de las 6 horas documentadas, si el intento ya existe y falta el identificador, no se publica otro POST.
    /// </summary>
    public static bool MayPostCreate(PayPalCreateBlock block, bool externalOrderIdKnown)
    {
        return block == PayPalCreateBlock.None && !externalOrderIdKnown;
    }

    public static string FormatEuros(long amountCents)
    {
        if (amountCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents));
        }

        var units = amountCents / 100;
        var fraction = amountCents % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{units}.{fraction:00}");
    }

    public static bool TryParseAmount(string? value, out long amountCents)
    {
        amountCents = 0;
        if (value is null || !AmountPattern.IsMatch(value))
        {
            return false;
        }

        var pieces = value.Split('.');
        if (!long.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var units)
            || !int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var fraction))
        {
            return false;
        }

        amountCents = (units * 100) + fraction;
        return true;
    }

    public static bool IsOrderId(string? value)
    {
        return value is not null && OrderIdPattern.IsMatch(value);
    }

    public static bool IsSandboxApprovalUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url))
        {
            return false;
        }

        return url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo)
            && string.Equals(url.Host, "www.sandbox.paypal.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSandboxPayerRedirect(PayPalOrderSnapshot order, PayPalReading reading)
    {
        return reading.Attention is null
            && reading.Evidence == PaymentEvidence.StillPayable
            && order.Status is "CREATED" or "SAVED" or "PAYER_ACTION_REQUIRED"
            && IsSandboxApprovalUrl(order.ApproveUrl);
    }

    public static bool IsSandboxCertificateUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url))
        {
            return false;
        }

        return url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo)
            && string.IsNullOrEmpty(url.Query)
            && string.IsNullOrEmpty(url.Fragment)
            && url.Host is "api.sandbox.paypal.com" or "api-m.sandbox.paypal.com"
            && url.AbsolutePath.StartsWith("/v1/notifications/certs/", StringComparison.Ordinal)
            && url.AbsolutePath.Length > "/v1/notifications/certs/".Length;
    }

    public static bool IsLiveApiHost(string? host)
    {
        return string.Equals(host, "api-m.paypal.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "api.paypal.com", StringComparison.OrdinalIgnoreCase);
    }

    public static PayPalReading Interpret(PayPalOrderSnapshot order, Guid orderId, string orderNumber, long amountCents)
    {
        if (order.PurchaseUnitCount != 1
            || !string.Equals(order.CustomId, CustomId(orderId), StringComparison.Ordinal)
            || !string.Equals(order.InvoiceId, orderNumber, StringComparison.Ordinal)
            || !IsOrderId(order.Id))
        {
            return Unknown(PayPalRules.IdentityMessage);
        }

        if (order.AmountCents != amountCents)
        {
            return Unknown(PayPalRules.AmountMessage);
        }

        if (!string.Equals(order.Currency, PayPalRules.Currency, StringComparison.Ordinal))
        {
            return Unknown(PayPalRules.CurrencyMessage);
        }

        if (order.Status is "CREATED" or "SAVED" or "PAYER_ACTION_REQUIRED")
        {
            return new PayPalReading(PaymentEvidence.StillPayable, null, null, false);
        }

        if (order.Status == "APPROVED")
        {
            return order.Captures.Count == 0
                ? new PayPalReading(PaymentEvidence.StillPayable, null, null, true)
                : new PayPalReading(PaymentEvidence.StillPayable, PayPalRules.UnknownStatusMessage, null, false);
        }

        if (order.Status == "VOIDED")
        {
            return order.Captures.Any(capture => capture.Status == "COMPLETED")
                ? Unknown(PayPalRules.UnknownStatusMessage)
                : new PayPalReading(PaymentEvidence.ClosedUnpaid, null, null, false);
        }

        if (order.Status != "COMPLETED")
        {
            return Unknown(PayPalRules.UnknownStatusMessage);
        }

        return InterpretCaptures(order.Captures, amountCents);
    }

    public static PayPalOrderSnapshot? ReadOrder(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProperty) || idProperty.GetString() is not { } id)
        {
            return null;
        }

        var status = root.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() ?? string.Empty : string.Empty;
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return new PayPalOrderSnapshot(id, status, null, null, null, null, ReadApproval(root), [], 0);
        }

        var count = units.GetArrayLength();
        if (count != 1)
        {
            return new PayPalOrderSnapshot(id, status, null, null, null, null, ReadApproval(root), [], count);
        }

        var unit = units[0];
        var customId = unit.TryGetProperty("custom_id", out var custom) ? custom.GetString() : null;
        var invoiceId = unit.TryGetProperty("invoice_id", out var invoice) ? invoice.GetString() : null;
        long? amount = null;
        string? currency = null;
        if (unit.TryGetProperty("amount", out var amountProperty))
        {
            currency = amountProperty.TryGetProperty("currency_code", out var currencyProperty) ? currencyProperty.GetString() : null;
            if (amountProperty.TryGetProperty("value", out var valueProperty) && TryParseAmount(valueProperty.GetString(), out var parsed))
            {
                amount = parsed;
            }
        }

        var captures = new List<PayPalCaptureView>();
        if (unit.TryGetProperty("payments", out var payments)
            && payments.TryGetProperty("captures", out var captureList)
            && captureList.ValueKind == JsonValueKind.Array)
        {
            foreach (var capture in captureList.EnumerateArray())
            {
                var captureId = capture.TryGetProperty("id", out var captureIdProperty) ? captureIdProperty.GetString() : null;
                var captureStatus = capture.TryGetProperty("status", out var captureStatusProperty) ? captureStatusProperty.GetString() : null;
                if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(captureStatus))
                {
                    continue;
                }

                long? captureAmount = null;
                string? captureCurrency = null;
                if (capture.TryGetProperty("amount", out var captureMoney))
                {
                    captureCurrency = captureMoney.TryGetProperty("currency_code", out var captureCurrencyProperty)
                        ? captureCurrencyProperty.GetString()
                        : null;
                    if (captureMoney.TryGetProperty("value", out var captureValue) && TryParseAmount(captureValue.GetString(), out var parsedCapture))
                    {
                        captureAmount = parsedCapture;
                    }
                }

                captures.Add(new PayPalCaptureView(captureId, captureStatus, captureAmount, captureCurrency));
            }
        }

        return new PayPalOrderSnapshot(id, status, customId, invoiceId, amount, currency, ReadApproval(root), captures, count);
    }

    public static PayPalEventNotice? ReadEvent(JsonElement root)
    {
        var eventId = root.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
        var eventType = root.TryGetProperty("event_type", out var typeProperty) ? typeProperty.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType) || !root.TryGetProperty("resource", out var resource))
        {
            return null;
        }

        string? paypalOrderId = null;
        if (eventType.StartsWith("CHECKOUT.ORDER.", StringComparison.Ordinal))
        {
            paypalOrderId = resource.TryGetProperty("id", out var resourceId) ? resourceId.GetString() : null;
        }
        else if (resource.TryGetProperty("supplementary_data", out var supplementary)
            && supplementary.TryGetProperty("related_ids", out var related)
            && related.TryGetProperty("order_id", out var relatedOrder))
        {
            paypalOrderId = relatedOrder.GetString();
        }

        if (!IsOrderId(paypalOrderId))
        {
            return null;
        }

        return new PayPalEventNotice(eventId, eventType, paypalOrderId!, ReadLocalOrderId(resource));
    }

    private static Guid? ReadLocalOrderId(JsonElement resource)
    {
        if (resource.TryGetProperty("custom_id", out var custom) && Guid.TryParse(custom.GetString(), out var direct))
        {
            return direct;
        }

        if (!resource.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array || units.GetArrayLength() != 1)
        {
            return null;
        }

        var unit = units[0];
        return unit.TryGetProperty("custom_id", out var nested) && Guid.TryParse(nested.GetString(), out var nestedId)
            ? nestedId
            : null;
    }

    public static bool AlreadyCaptured(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("issue", out var issue)
                    && string.Equals(issue.GetString(), "ORDER_ALREADY_CAPTURED", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320u & mask);
            }
        }

        return ~crc;
    }

    public static string Message(string transmissionId, string transmissionTime, string webhookId, ReadOnlySpan<byte> body)
    {
        if (!SafeField(transmissionId) || !SafeField(transmissionTime) || !SafeField(webhookId))
        {
            throw new PayPalSignatureException();
        }

        return string.Create(CultureInfo.InvariantCulture, $"{transmissionId}|{transmissionTime}|{webhookId}|{Crc32(body)}");
    }

    public static bool Verify(PayPalTransmission transmission, ReadOnlyMemory<byte> body, string webhookId, string certificatePem)
    {
        if (!string.Equals(transmission.Algorithm, PayPalRules.Algorithm, StringComparison.Ordinal)
            || !IsSandboxCertificateUrl(transmission.CertificateUrl)
            || string.IsNullOrWhiteSpace(transmission.Signature)
            || certificatePem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            return false;
        }

        string message;
        try
        {
            message = Message(transmission.TransmissionId!, transmission.TransmissionTime!, webhookId, body.Span);
        }
        catch (PayPalSignatureException)
        {
            return false;
        }

        try
        {
            var signature = Convert.FromBase64String(transmission.Signature);
            using var certificate = X509Certificate2.CreateFromPem(certificatePem);
            using var rsa = certificate.GetRSAPublicKey();
            return rsa is not null
                && rsa.VerifyData(Encoding.UTF8.GetBytes(message), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static PayPalReading InterpretCaptures(IReadOnlyList<PayPalCaptureView> captures, long amountCents)
    {
        if (captures.Count == 0)
        {
            return Unknown(PayPalRules.UnknownStatusMessage);
        }

        foreach (var capture in captures)
        {
            if (capture.Status is not ("COMPLETED" or "DECLINED" or "FAILED" or "PENDING" or "REFUNDED" or "PARTIALLY_REFUNDED"))
            {
                return Unknown(PayPalRules.UnknownStatusMessage);
            }

            if (capture.AmountCents is null || capture.AmountCents != amountCents)
            {
                return Unknown(PayPalRules.AmountMessage);
            }

            if (!string.Equals(capture.Currency, PayPalRules.Currency, StringComparison.Ordinal))
            {
                return Unknown(PayPalRules.CurrencyMessage);
            }
        }

        if (captures.Any(capture => capture.Status is "REFUNDED" or "PARTIALLY_REFUNDED"))
        {
            return Unknown(PayPalRules.RefundMessage);
        }

        if (captures.Any(capture => capture.Status == "PENDING"))
        {
            return Unknown(PayPalRules.PendingCaptureMessage);
        }

        var completed = captures.Where(capture => capture.Status == "COMPLETED").ToArray();
        if (completed.Length == 1)
        {
            return new PayPalReading(PaymentEvidence.Paid, null, completed[0].Id, false);
        }

        if (completed.Length > 1)
        {
            return Unknown(PayPalRules.UnknownStatusMessage);
        }

        return captures.All(capture => capture.Status is "DECLINED" or "FAILED")
            ? new PayPalReading(PaymentEvidence.ClosedUnpaid, null, null, false)
            : Unknown(PayPalRules.UnknownStatusMessage);
    }

    private static PayPalReading Unknown(string attention)
    {
        return new PayPalReading(PaymentEvidence.Unknown, attention, null, false);
    }

    private static string? ReadApproval(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? approve = null;
        foreach (var link in links.EnumerateArray())
        {
            var rel = link.TryGetProperty("rel", out var relProperty) ? relProperty.GetString() : null;
            var href = link.TryGetProperty("href", out var hrefProperty) ? hrefProperty.GetString() : null;
            if (rel == "payer-action")
            {
                return href;
            }

            if (rel == "approve")
            {
                approve = href;
            }
        }

        return approve;
    }

    private static string Fit(string value)
    {
        if (value.Length > PayPalRules.RequestIdMaxLength)
        {
            throw new InvalidOperationException(PayPalRules.UnknownStatusMessage);
        }

        return value;
    }

    private static bool SafeField(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character => character is >= ' ' and <= '~' && character != '|');
    }
}
