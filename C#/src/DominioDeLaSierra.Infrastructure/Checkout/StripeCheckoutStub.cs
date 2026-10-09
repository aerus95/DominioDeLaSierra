using DominioDeLaSierra.Application.Checkout;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Sustituto de Stripe para las pruebas. No llama a la red y reutiliza la misma sesión
/// cuando la clave de idempotencia se repite.
/// </summary>
public sealed class StripeCheckoutStub : IStripeCheckoutGateway
{
    private readonly object gate = new();
    private readonly Dictionary<string, CreatedStripeCheckout> sessions = [];
    private readonly Dictionary<string, StripeSessionInspection> bySession = [];
    private readonly Dictionary<Guid, StripeSessionInspection> byOrder = [];
    private readonly List<(string Key, long AmountCents)> calls = [];
    private int sequence;
    private int inspectStarted;

    public Exception? Failure { get; set; }
    public Exception? InspectFailure { get; set; }
    public TimeSpan Delay { get; set; }
    public TaskCompletionSource<bool>? InspectHold { get; set; }
    public int InspectStarted => Volatile.Read(ref inspectStarted);

    public IReadOnlyList<(string Key, long AmountCents)> Calls
    {
        get
        {
            lock (gate)
            {
                return calls.ToArray();
            }
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            sessions.Clear();
            bySession.Clear();
            byOrder.Clear();
            calls.Clear();
            sequence = 0;
            Failure = null;
            InspectFailure = null;
            Delay = TimeSpan.Zero;
            InspectHold = null;
            inspectStarted = 0;
        }
    }

    public void SetInspection(Guid orderId, StripeSessionInspection inspection)
    {
        lock (gate)
        {
            byOrder[orderId] = inspection;
            if (!string.IsNullOrWhiteSpace(inspection.SessionId))
            {
                bySession[inspection.SessionId] = inspection;
            }
        }
    }

    public async Task<StripeSessionInspection> InspectAsync(
        StripeSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref inspectStarted);
        if (InspectHold is not null)
        {
            await InspectHold.Task.WaitAsync(cancellationToken);
        }

        if (InspectFailure is not null)
        {
            throw InspectFailure;
        }

        lock (gate)
        {
            if (!string.IsNullOrWhiteSpace(query.SessionId) && bySession.TryGetValue(query.SessionId, out var stored))
            {
                return stored;
            }

            if (byOrder.TryGetValue(query.OrderId, out var recovered))
            {
                return recovered;
            }

            return StripeSessionInspection.NotFound();
        }
    }

    public async Task<CreatedStripeCheckout> CreateAsync(
        StripeCheckoutDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        lock (gate)
        {
            calls.Add((idempotencyKey, draft.AmountCents));
            if (Failure is not null)
            {
                throw Failure;
            }

            if (!sessions.TryGetValue(idempotencyKey, out var existing))
            {
                sequence++;
                var sessionId = $"cs_test_stub{sequence:x}";
                existing = new CreatedStripeCheckout(
                    sessionId,
                    $"https://checkout.stripe.com/c/pay/{sessionId}",
                    draft.AmountCents,
                    draft.ExpiresAt);
                sessions[idempotencyKey] = existing;
            }

            return existing;
        }
    }
}
