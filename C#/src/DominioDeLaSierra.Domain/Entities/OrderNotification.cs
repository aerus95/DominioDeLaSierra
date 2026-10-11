using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class OrderNotification
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public OrderNotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? ProcessingUntil { get; private set; }
    public uint RowVersion { get; private set; }

    public string ProviderIdempotencyKey => OrderNotificationRules.ProviderIdempotencyKey(Id);

    public Order Order { get; private set; } = null!;

    private OrderNotification()
    {
    }

    internal static OrderNotification CreatePending(
        Guid id,
        Order order,
        OrderNotificationKind kind,
        DateTimeOffset createdAt)
    {
        OrderText.RequireId(id, "La notificación");
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("El tipo de notificación no es válido.");
        }

        return new OrderNotification
        {
            Id = id,
            Order = order,
            OrderId = order.Id,
            Kind = kind,
            Status = OrderNotificationStatus.Pending,
            Attempts = 0,
            NextAttemptAt = createdAt,
            CreatedAt = createdAt
        };
    }

    public void Claim(DateTimeOffset now)
    {
        EnsureOperable(now);
        if (!OrderNotificationRules.CanClaim(Status, ProcessingUntil, NextAttemptAt, now))
        {
            throw new InvalidOperationException("La notificación no se puede tomar.");
        }

        Status = OrderNotificationStatus.Processing;
        ProcessingUntil = now.Add(OrderNotificationRules.ProcessingLease);
    }

    public void MarkSent(DateTimeOffset at, string providerMessageId)
    {
        EnsureOperable(at);
        if (Status != OrderNotificationStatus.Processing)
        {
            throw new InvalidOperationException("La notificación no está en envío.");
        }

        ProviderMessageId = OrderText.RequireToken(
            providerMessageId,
            OrderNotificationRules.ProviderMessageIdMaxLength,
            "El identificador del proveedor");
        SentAt = at;
        Status = OrderNotificationStatus.Sent;
        ProcessingUntil = null;
        LastError = null;
    }

    public void RecordFailure(DateTimeOffset now, string error)
    {
        EnsureOperable(now);
        if (Status != OrderNotificationStatus.Processing)
        {
            throw new InvalidOperationException("La notificación no está en envío.");
        }

        var sanitized = OrderNotificationRules.SanitizeError(error);
        var attempts = checked(Attempts + 1);
        Attempts = attempts;
        LastError = sanitized;
        ProcessingUntil = null;
        if (attempts >= OrderNotificationRules.MaxAttempts)
        {
            Status = OrderNotificationStatus.Abandoned;
            NextAttemptAt = null;
            return;
        }

        Status = OrderNotificationStatus.Failed;
        NextAttemptAt = now.Add(OrderNotificationRules.RetryDelay(attempts));
    }

    private void EnsureOperable(DateTimeOffset at)
    {
        if (at < CreatedAt)
        {
            throw new ArgumentException("La fecha del envío no es válida.");
        }
    }
}
