using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class ReservationHoldIncidentConfiguration : IEntityTypeConfiguration<ReservationHoldIncident>
{
    public void Configure(EntityTypeBuilder<ReservationHoldIncident> builder)
    {
        builder.ToTable("ReservationHoldIncidents", table =>
        {
            table.HasCheckConstraint(
                "CK_ReservationHoldIncidents_Severity",
                """
                ("Severity" = 'Held' AND "Reconciliation" = 'Holding')
                OR ("Severity" = 'Critical' AND "Reconciliation" = 'PaidUnsettled')
                """);
            table.HasCheckConstraint(
                "CK_ReservationHoldIncidents_Provider",
                """
                "Provider" IS NULL OR "Provider" IN ('Stripe', 'Redsys', 'PayPal')
                """);
            table.HasCheckConstraint(
                "CK_ReservationHoldIncidents_Age",
                """
                "AgeSeconds" >= 0 AND "AlertCount" >= 1 AND "NextReviewAt" >= "OpenedAt" AND "UpdatedAt" >= "OpenedAt"
                """);
            table.HasCheckConstraint(
                "CK_ReservationHoldIncidents_Resolution",
                """
                ("ResolvedAt" IS NULL AND "Resolution" IS NULL)
                OR ("ResolvedAt" IS NOT NULL AND "ResolvedAt" >= "OpenedAt" AND "Resolution" IN ('Verified', 'Administrative'))
                """);
            table.HasCheckConstraint(
                "CK_ReservationHoldIncidents_Reference",
                """
                "ExternalReference" IS NULL OR "ExternalReference" ~ '^[A-Za-z0-9]{4,36}$'
                """);
        });

        builder.HasKey(incident => incident.Id);

        builder.Property(incident => incident.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(incident => incident.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(incident => incident.Provider)
            .HasColumnType("character varying(16)")
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(incident => incident.Severity)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(incident => incident.Reconciliation)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(incident => incident.AgeSeconds)
            .IsRequired();

        builder.Property(incident => incident.OpenedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(incident => incident.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(incident => incident.NextReviewAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(incident => incident.ResolvedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(incident => incident.Resolution)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32);

        builder.Property(incident => incident.AlertCount)
            .IsRequired();

        builder.Property(incident => incident.ExternalReference)
            .HasColumnType("character varying(36)")
            .HasMaxLength(36);

        builder.HasIndex(incident => incident.OrderId)
            .IsUnique()
            .HasFilter("\"ResolvedAt\" IS NULL")
            .HasDatabaseName("IX_ReservationHoldIncidents_OpenOrder");

        builder.HasOne(incident => incident.Order)
            .WithMany()
            .HasForeignKey(incident => incident.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
