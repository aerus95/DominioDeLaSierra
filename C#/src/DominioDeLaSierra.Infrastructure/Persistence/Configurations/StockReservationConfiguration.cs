using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", table =>
        {
            table.HasCheckConstraint(
                "CK_StockReservations_StatusTimes",
                """
                ("Status" = 'Reserved' AND "ConfirmationStartedAt" IS NULL AND "ConfirmedAt" IS NULL AND "ReleasedAt" IS NULL)
                OR ("Status" = 'Confirming' AND "ConfirmationStartedAt" IS NOT NULL AND "ConfirmedAt" IS NULL AND "ReleasedAt" IS NULL)
                OR ("Status" = 'Confirmed' AND "ConfirmationStartedAt" IS NOT NULL AND "ConfirmedAt" IS NOT NULL AND "ReleasedAt" IS NULL)
                OR ("Status" = 'Released' AND "ReleasedAt" IS NOT NULL AND "ConfirmedAt" IS NULL AND "ConfirmationStartedAt" IS NULL)
                """);
        });

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(reservation => reservation.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(reservation => reservation.Status)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(reservation => reservation.ReservedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(reservation => reservation.ConfirmationStartedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(reservation => reservation.ConfirmedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(reservation => reservation.ReleasedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(reservation => reservation.OrderId)
            .IsUnique();

        builder.HasIndex(reservation => reservation.Status)
            .HasFilter("\"Status\" = 'Reserved'");

        builder.HasOne(reservation => reservation.Order)
            .WithMany()
            .HasForeignKey(reservation => reservation.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
