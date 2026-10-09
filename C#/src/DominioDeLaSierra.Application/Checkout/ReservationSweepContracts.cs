namespace DominioDeLaSierra.Application.Checkout;

public static class ReservationSweepRules
{
    public static readonly TimeSpan MissingSessionGrace = TimeSpan.FromMinutes(2);
    public const int DefaultIntervalSeconds = 60;
    public const int DefaultBatchSize = 20;
    public const string UnresolvedMessage = "El estado de Stripe no permite liberar ni confirmar la reserva.";
    public const string ConfirmingMessage = "La reserva está en confirmación y Stripe no muestra un cobro.";

    public static bool WaitForMissingSession(bool sessionStored, StripeCheckoutFact fact, DateTimeOffset now, DateTimeOffset reservationExpiresAt)
    {
        return !sessionStored
            && fact is StripeCheckoutFact.NotFound or StripeCheckoutFact.Unavailable
            && now < reservationExpiresAt.Add(MissingSessionGrace);
    }
}

public sealed class ReservationSweepOptions
{
    public const string SectionName = "Reservations";

    public bool SweepEnabled { get; set; }

    public int SweepIntervalSeconds { get; set; } = ReservationSweepRules.DefaultIntervalSeconds;

    public int BatchSize { get; set; } = ReservationSweepRules.DefaultBatchSize;
}

public sealed record ReservationSweepResult(int Processed, int Released, int Confirmed, int Failed, int Incidents);

public interface IReservationSweep
{
    Task<ReservationSweepResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
