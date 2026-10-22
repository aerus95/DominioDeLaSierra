using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class ProviderReconciliationConfiguration : IEntityTypeConfiguration<ProviderReconciliation>
{
    public void Configure(EntityTypeBuilder<ProviderReconciliation> builder)
    {
        builder.ToTable("ProviderReconciliations", table =>
        {
            table.HasCheckConstraint(
                "CK_ProviderReconciliations_Provider",
                """
                "Provider" IN ('Redsys', 'PayPal')
                """);
            table.HasCheckConstraint(
                "CK_ProviderReconciliations_Evidence",
                """
                "Evidence" IN ('Unknown', 'StillPayable', 'Paid', 'ClosedUnpaid', 'Unavailable')
                """);
            table.HasCheckConstraint(
                "CK_ProviderReconciliations_Attempt",
                """
                "AttemptCount" >= 1 AND "NextAttemptAt" >= "LastAttemptAt"
                """);
            table.HasCheckConstraint(
                "CK_ProviderReconciliations_Reference",
                """
                "ExternalReference" ~ '^[A-Za-z0-9]{4,36}$'
                """);
        });

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.PaymentId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.Provider)
            .HasColumnType("character varying(16)")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(item => item.ExternalReference)
            .HasColumnType("character varying(36)")
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(item => item.Evidence)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(item => item.AttemptCount)
            .IsRequired();

        builder.Property(item => item.LastAttemptAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(item => item.NextAttemptAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(item => item.Attention)
            .HasColumnType("character varying(240)")
            .HasMaxLength(240);

        builder.HasIndex(item => item.PaymentId)
            .IsUnique()
            .HasDatabaseName("IX_ProviderReconciliations_Payment");

        builder.HasIndex(item => item.NextAttemptAt)
            .HasDatabaseName("IX_ProviderReconciliations_NextAttempt");

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(item => item.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
