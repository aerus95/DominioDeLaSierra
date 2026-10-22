namespace DominioDeLaSierra.Domain;

/// <summary>
/// La creación de Orders v2 guarda PayPal-Request-Id durante 6 horas.
/// Pasado ese plazo, o si el cuerpo cambia, PayPal no documenta que el POST devuelva la misma orden.
/// Sin el identificador de la orden, este sistema no repite el POST: repetirlo no es un GET.
/// </summary>
public enum PayPalCreateOutcome
{
    Attempted,
    Uncertain,
    Identified
}

public enum PayPalCreateBlock
{
    None,
    InsideDocumentedWindow,
    OutsideDocumentedWindow
}

public static class PayPalCreateMarkers
{
    public static readonly TimeSpan DocumentedIdempotencyWindow = TimeSpan.FromHours(6);

    public static string EventId(Guid paymentId)
    {
        return "paypal-create:" + paymentId.ToString("N");
    }

    public static bool WithinDocumentedWindow(DateTimeOffset attemptedAt, DateTimeOffset now)
    {
        return now <= attemptedAt.Add(DocumentedIdempotencyWindow);
    }
}
