namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Simula un fallo antes del commit del barrido. En producción permanece a cero.
/// </summary>
public sealed class ReservationSweepFailureProbe
{
    private int remaining;

    public void FailNext(int count = 1)
    {
        Interlocked.Exchange(ref remaining, count);
    }

    public bool ConsumeFailure()
    {
        while (true)
        {
            var current = Volatile.Read(ref remaining);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref remaining, current - 1, current) == current)
            {
                return true;
            }
        }
    }
}
