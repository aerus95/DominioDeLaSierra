namespace DominioDeLaSierra.Domain;

/// <summary>
/// La ventana comercial de pago es de 30 minutos desde que se abre Checkout.
/// Stripe exige que expires_at sea al menos esos 30 minutos después de crear la sesión en su reloj,
/// y la reserva tiene que existir antes de esa llamada. Por eso las dos caducidades no pueden ser iguales:
/// la sesión pide 30 minutos más un margen de creación, y la reserva termina después, con una cola de reconciliación.
/// Ninguno de los dos márgenes alarga el tiempo de compra anterior al pago ni se renueva en cada reintento.
/// </summary>
public static class StripeCheckoutExpiry
{
    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Maximum = TimeSpan.FromHours(24);
    public static readonly TimeSpan SessionCreationSlack = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ReservationTail = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PaymentHold = Minimum + SessionCreationSlack + ReservationTail;

    public const string ExpiredMessage = "La reserva del pedido ha caducado.";
    public const string OutsideStripeWindowMessage = "La reserva ya no cubre el tiempo mínimo de Stripe Checkout. El pedido sigue pendiente y no se ha iniciado el pago.";

    public static DateTimeOffset PaymentSessionExpiry(DateTimeOffset utcNow, DateTimeOffset reservationExpiresAt)
    {
        if (reservationExpiresAt <= utcNow)
        {
            throw new InvalidOperationException(ExpiredMessage);
        }

        var earliest = utcNow.Add(Minimum);
        var preferred = earliest.Add(SessionCreationSlack);
        var latest = reservationExpiresAt.Subtract(ReservationTail);
        var stripeLatest = utcNow.Add(Maximum);
        if (latest > stripeLatest)
        {
            latest = stripeLatest;
        }

        if (earliest > latest)
        {
            throw new InvalidOperationException(OutsideStripeWindowMessage);
        }

        return preferred <= latest ? preferred : latest;
    }
}
