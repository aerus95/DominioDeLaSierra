using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class FulfillmentTransitionConfiguration : IEntityTypeConfiguration<FulfillmentTransition>
{
    public void Configure(EntityTypeBuilder<FulfillmentTransition> builder)
    {
        builder.ToTable("FulfillmentTransitions", table =>
        {
            table.HasCheckConstraint(
                "CK_FulfillmentTransitions_Step",
                """
                ("FromStatus" = 'Unfulfilled' AND "ToStatus" = 'Preparing')
                OR ("FromStatus" = 'Preparing' AND "ToStatus" = 'Prepared')
                OR ("FromStatus" = 'Prepared' AND "ToStatus" = 'Shipped')
                """);
        });

        builder.HasKey(transition => transition.Id);

        builder.Property(transition => transition.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(transition => transition.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(transition => transition.FromStatus)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(transition => transition.ToStatus)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(transition => transition.OccurredAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(transition => transition.ActorUserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(transition => transition.Carrier)
            .HasColumnType($"character varying({Order.CarrierMaxLength})")
            .HasMaxLength(Order.CarrierMaxLength);

        builder.Property(transition => transition.TrackingNumber)
            .HasColumnType($"character varying({Order.TrackingNumberMaxLength})")
            .HasMaxLength(Order.TrackingNumberMaxLength);

        builder.HasIndex(transition => new { transition.OrderId, transition.OccurredAt });
    }
}
