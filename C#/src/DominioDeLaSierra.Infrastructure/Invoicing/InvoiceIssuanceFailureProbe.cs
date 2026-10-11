namespace DominioDeLaSierra.Infrastructure.Invoicing;

/// <summary>
/// Simula un fallo después de reservar el número y antes de guardar la factura.
/// En producción permanece a cero y no consume numeración.
/// </summary>
public sealed class InvoiceIssuanceFailureProbe
{
    private int remaining;

    public void FailNext(int count = 1)
    {
        Interlocked.Exchange(ref remaining, count);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref remaining, 0);
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
