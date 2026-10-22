using System.Globalization;
using System.Text.Json;

namespace DominioDeLaSierra.Application.Checkout;

public enum RedsysOutcome
{
    Authorized,
    Denied,
    Unknown
}

/// <summary>
/// 0000 es la autorización de una compra. 0001-0099 pertenecían al tramo antiguo
/// y quedan en Unknown hasta que el banco contratado los confirme: no autorizan ni deniegan.
/// 0400 es una anulación aceptada y 0900 una devolución o confirmación aceptada.
/// Ninguno de los dos cierra una compra. El resto de códigos numéricos son denegaciones
/// o errores inequívocos. Un valor no numérico no cierra la operación.
/// </summary>
public static class RedsysResponse
{
    public static RedsysOutcome Classify(string? response)
    {
        var text = response?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Any(character => !char.IsDigit(character)))
        {
            return RedsysOutcome.Unknown;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
        {
            return RedsysOutcome.Unknown;
        }

        if (code == 0)
        {
            return RedsysOutcome.Authorized;
        }

        if (code is >= 1 and <= 99 || code is 400 or 900)
        {
            return RedsysOutcome.Unknown;
        }

        return RedsysOutcome.Denied;
    }
}

public sealed record RedsysMerchantParameters(
    string Order,
    long AmountCents,
    string Currency,
    string MerchantCode,
    string Terminal,
    string? Response,
    string? TransactionType,
    string? AuthorisationCode);

public static class RedsysParameterReader
{
    public static RedsysMerchantParameters Read(JsonElement root)
    {
        var amountText = Text(root, "Ds_Amount", "DS_MERCHANT_AMOUNT");
        if (!long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            amount = -1;
        }

        return new RedsysMerchantParameters(
            Text(root, "Ds_Order", "DS_MERCHANT_ORDER") ?? string.Empty,
            amount,
            Text(root, "Ds_Currency", "DS_MERCHANT_CURRENCY") ?? string.Empty,
            Text(root, "Ds_MerchantCode", "DS_MERCHANT_MERCHANTCODE") ?? string.Empty,
            Text(root, "Ds_Terminal", "DS_MERCHANT_TERMINAL") ?? string.Empty,
            Text(root, "Ds_Response"),
            Text(root, "Ds_TransactionType", "DS_MERCHANT_TRANSACTIONTYPE"),
            SafeAuthorisation(Text(root, "Ds_AuthorisationCode")));
    }

    public static bool SameTerminal(string configured, string received)
    {
        if (int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var left)
            && int.TryParse(received, NumberStyles.None, CultureInfo.InvariantCulture, out var right))
        {
            return left == right;
        }

        return string.Equals(configured.Trim(), received.Trim(), StringComparison.Ordinal);
    }

    private static string? SafeAuthorisation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length is < 1 or > 10 || text.Any(character => !char.IsLetterOrDigit(character)))
        {
            return null;
        }

        if (text.All(char.IsDigit) && text.Length is >= 13 and <= 19)
        {
            return null;
        }

        return text;
    }

    private static string? Text(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
            }
        }

        return null;
    }
}

public static class RedsysNotificationRules
{
    public const string UnavailableMessage = "No se ha podido registrar la notificación de Redsys.";
    public const string AmountMessage = "El importe de Redsys no coincide con el pedido.";
    public const string CurrencyMessage = "La moneda de Redsys no es EUR.";
    public const string MerchantMessage = "El comercio o el terminal de Redsys no coinciden.";
    public const string UnknownMessage = "El resultado de Redsys no permite confirmar ni liberar la reserva.";
    public const string StockMessage = "El cobro de Redsys no tiene stock reservado. Hace falta reconciliar.";
    public const string Ignored = "ignored";
    public const string Confirmed = "confirmed";
    public const string Denied = "denied";
    public const string Duplicate = "duplicate";
    public const string NeedsReconciliation = "reconciliation";
}

public interface IRedsysNotifications
{
    Task<string> ReceiveAsync(
        string? signatureVersion,
        string? merchantParameters,
        string? signature,
        CancellationToken cancellationToken = default);
}
