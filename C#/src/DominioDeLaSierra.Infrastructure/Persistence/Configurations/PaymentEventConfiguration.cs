using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("PaymentEvents");

        builder.HasKey(paymentEvent => paymentEvent.Id);

        builder.Property(paymentEvent => paymentEvent.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(paymentEvent => paymentEvent.PaymentId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(paymentEvent => paymentEvent.ExternalEventId)
            .HasColumnType($"character varying({PaymentEvent.ExternalEventIdMaxLength})")
            .HasMaxLength(PaymentEvent.ExternalEventIdMaxLength)
            .IsRequired();

        builder.Property(paymentEvent => paymentEvent.EventType)
            .HasColumnType($"character varying({PaymentEvent.EventTypeMaxLength})")
            .HasMaxLength(PaymentEvent.EventTypeMaxLength)
            .IsRequired();

        builder.Property(paymentEvent => paymentEvent.ReceivedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(paymentEvent => paymentEvent.ProcessedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(paymentEvent => paymentEvent.AttentionReason)
            .HasColumnType($"character varying({PaymentEvent.AttentionReasonMaxLength})")
            .HasMaxLength(PaymentEvent.AttentionReasonMaxLength);

        builder.HasIndex(paymentEvent => paymentEvent.ExternalEventId)
            .IsUnique();
    }
}
