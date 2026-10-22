namespace DominioDeLaSierra.Domain.Entities;

public sealed class ReservationHoldIncident
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public PaymentProvider? Provider { get; private set; }
    public string Severity { get; private set; } = null!;
    public string Reconciliation { get; private set; } = null!;
    public long AgeSeconds { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextReviewAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? Resolution { get; private set; }
    public int AlertCount { get; private set; }
    public string? ExternalReference { get; private set; }

    public Order Order { get; private set; } = null!;

    private ReservationHoldIncident()
    {
    }

    public static ReservationHoldIncident Open(
        Guid id,
        Guid orderId,
        PaymentProvider? provider,
        string severity,
        string reconciliation,
        long ageSeconds,
        DateTimeOffset now,
        DateTimeOffset nextReviewAt,
        string? externalReference = null)
    {
        OrderText.RequireId(id, "La incidencia");
        OrderText.RequireId(orderId, "El pedido");
        if (ageSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ageSeconds));
        }

        if (nextReviewAt < now)
        {
            throw new ArgumentException("La próxima revisión no puede ser anterior a la apertura.");
        }

        return new ReservationHoldIncident
        {
            Id = id,
            OrderId = orderId,
            Provider = provider,
            Severity = RequireKind(severity),
            Reconciliation = RequireKind(reconciliation),
            AgeSeconds = ageSeconds,
            OpenedAt = now,
            UpdatedAt = now,
            NextReviewAt = nextReviewAt,
            AlertCount = 1,
            ExternalReference = OpaqueReference(externalReference)
        };
    }

    public bool IsOpen => ResolvedAt is null;

    public bool AwaitingReview(DateTimeOffset now)
    {
        return IsOpen && now >= NextReviewAt;
    }

    public void Review(
        PaymentProvider? provider,
        string severity,
        string reconciliation,
        long ageSeconds,
        DateTimeOffset now,
        DateTimeOffset nextReviewAt,
        string? externalReference = null)
    {
        EnsureOpen();
        if (ageSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ageSeconds));
        }

        if (nextReviewAt < now)
        {
            throw new ArgumentException("La próxima revisión no puede ser anterior a esta.");
        }

        Provider = provider;
        Severity = RequireKind(severity);
        Reconciliation = RequireKind(reconciliation);
        AgeSeconds = ageSeconds;
        UpdatedAt = now;
        NextReviewAt = nextReviewAt;
        AlertCount++;
        RememberReference(externalReference);
    }

    public void RememberReference(string? externalReference)
    {
        EnsureOpen();
        var reference = OpaqueReference(externalReference);
        if (reference is not null)
        {
            ExternalReference = reference;
        }
    }

    public void ResolveVerified(DateTimeOffset at)
    {
        Resolve(at, "Verified");
    }

    public void ResolveAdministratively(DateTimeOffset at)
    {
        Resolve(at, "Administrative");
    }

    private void Resolve(DateTimeOffset at, string resolution)
    {
        EnsureOpen();
        if (at < OpenedAt)
        {
            throw new ArgumentException("La resolución no puede ser anterior a la apertura.");
        }

        ResolvedAt = at;
        Resolution = resolution;
        UpdatedAt = at;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("La incidencia ya está resuelta.");
        }
    }

    private static string RequireKind(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 32)
        {
            throw new ArgumentException("El estado de la incidencia no es válido.");
        }

        return text;
    }

    public static string? OpaqueReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length is < 4 or > 36 || text.Any(character => !char.IsLetterOrDigit(character)))
        {
            return null;
        }

        return text;
    }
}
