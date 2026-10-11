using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    private static readonly string StateConstraint = $"""
        "Kind" IN ('PaidConfirmation')
        AND "Attempts" >= 0
        AND "Attempts" <= {OrderNotificationRules.MaxAttempts}
        AND (
            ("Status" = 'Pending'
                AND "Attempts" = 0
                AND "NextAttemptAt" IS NOT NULL
                AND "SentAt" IS NULL
                AND "ProviderMessageId" IS NULL
                AND "LastError" IS NULL
                AND "ProcessingUntil" IS NULL)
            OR ("Status" = 'Processing'
                AND "Attempts" < {OrderNotificationRules.MaxAttempts}
                AND "NextAttemptAt" IS NOT NULL
                AND "ProcessingUntil" IS NOT NULL
                AND "SentAt" IS NULL
                AND "ProviderMessageId" IS NULL)
            OR ("Status" = 'Sent'
                AND "SentAt" IS NOT NULL
                AND "ProviderMessageId" IS NOT NULL
                AND "ProcessingUntil" IS NULL
                AND "LastError" IS NULL)
            OR ("Status" = 'Failed'
                AND "Attempts" >= 1
                AND "Attempts" < {OrderNotificationRules.MaxAttempts}
                AND "NextAttemptAt" IS NOT NULL
                AND "LastError" IS NOT NULL
                AND "SentAt" IS NULL
                AND "ProviderMessageId" IS NULL
                AND "ProcessingUntil" IS NULL)
            OR ("Status" = 'Abandoned'
                AND "Attempts" = {OrderNotificationRules.MaxAttempts}
                AND "NextAttemptAt" IS NULL
                AND "LastError" IS NOT NULL
                AND "SentAt" IS NULL
                AND "ProviderMessageId" IS NULL
                AND "ProcessingUntil" IS NULL)
        )
        """;

    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications", table =>
        {
            table.HasCheckConstraint("CK_OrderNotifications_State", StateConstraint);
        });

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(notification => notification.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(notification => notification.Kind)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(notification => notification.Status)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(notification => notification.Attempts)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(notification => notification.NextAttemptAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(notification => notification.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(notification => notification.SentAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(notification => notification.ProviderMessageId)
            .HasColumnType($"character varying({OrderNotificationRules.ProviderMessageIdMaxLength})")
            .HasMaxLength(OrderNotificationRules.ProviderMessageIdMaxLength);

        builder.Property(notification => notification.LastError)
            .HasColumnType($"character varying({OrderNotificationRules.LastErrorMaxLength})")
            .HasMaxLength(OrderNotificationRules.LastErrorMaxLength);

        builder.Property(notification => notification.ProcessingUntil)
            .HasColumnType("timestamp with time zone");

        builder.Property(notification => notification.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.HasIndex(notification => new { notification.OrderId, notification.Kind })
            .IsUnique();

        builder.HasIndex(notification => notification.NextAttemptAt)
            .HasDatabaseName("IX_OrderNotifications_Due")
            .HasFilter("\"Status\" IN ('Pending', 'Failed')");

        builder.HasIndex(notification => notification.ProcessingUntil)
            .HasDatabaseName("IX_OrderNotifications_Processing")
            .HasFilter("\"Status\" = 'Processing'");
    }
}
