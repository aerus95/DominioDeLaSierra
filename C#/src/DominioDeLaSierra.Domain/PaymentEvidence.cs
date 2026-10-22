namespace DominioDeLaSierra.Domain;

/// <summary>
/// Evidencia bastante para decidir el stock. El retorno del navegador no es evidencia.
/// Unknown conserva la reserva: no sabemos si el pago todavía puede completarse.
/// </summary>
public enum PaymentEvidence
{
    Unknown,
    StillPayable,
    Paid,
    ClosedUnpaid
}

public static class PaymentRelease
{
    public static bool MayReleaseStock(PaymentEvidence evidence)
    {
        return evidence == PaymentEvidence.ClosedUnpaid;
    }
}

public static class ExternalHoldRules
{
    public const string UncertainHoldMessage = "Hay una operación de pago externa sin confirmar. La reserva no se libera.";
}

public static class PaymentConfirmation
{
    public static bool BrowserReturnConfirms => false;
}

public static class PaymentRefundPolicy
{
    public const string UnavailableMessage = "El reembolso de este proveedor no está implementado.";

    public static bool IsImplemented(PaymentProvider provider)
    {
        return provider switch
        {
            PaymentProvider.Stripe => false,
            PaymentProvider.Redsys => false,
            PaymentProvider.PayPal => false,
            _ => false
        };
    }
}

public static class PaymentProviderRules
{
    public const string RequiredMessage = "Hay que indicar el proveedor de pago.";
    public const string UnknownMessage = "El proveedor de pago no es válido.";
    public const string UnavailableMessage = "El proveedor de pago no está disponible.";
    public const string HoldMessage = "Este proveedor de pago no tiene una ventana de reserva configurada.";
    public const string NotStripeMessage = "Este pago no usa Stripe.";
    public const string NotPayPalMessage = "Este pago no usa PayPal.";
    public const string ExternalAssignedMessage = "La referencia externa del pago ya está asignada.";
    public const string StripeReferenceMessage = "Este pago ya tiene una referencia de Stripe.";
    public const string ExternalProviderMessage = "Este pago no admite una referencia externa de pasarela.";
    public const string ExternalMismatchMessage = "La referencia externa no corresponde a este proveedor.";
    public const string CardMessage = "No se almacenan números de tarjeta.";
    public const string RedsysMismatchMessage = "El número de operación de Redsys no corresponde a este proveedor.";

    public static bool TryParse(string? value, out PaymentProvider provider)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse(value.Trim(), ignoreCase: false, out provider)
            || !Enum.IsDefined(provider))
        {
            provider = default;
            return false;
        }

        return true;
    }
}

/// <summary>
/// La reserva de Stripe dura 34 minutos porque su sesión exige 30 y hay margen de creación y de reconciliación.
/// Redsys y PayPal no heredan esa ventana: no se inicia su pago hasta conocer el contrato de cada pasarela.
/// </summary>
public static class PaymentProviderHolds
{
    public static TimeSpan ReservationHold(PaymentProvider provider)
    {
        if (provider == PaymentProvider.Stripe)
        {
            return StripeCheckoutExpiry.PaymentHold;
        }

        throw new InvalidOperationException(PaymentProviderRules.HoldMessage);
    }
}
