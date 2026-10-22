using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "\"AmountCents\" >= 0");
            table.HasCheckConstraint("CK_Payments_Currency", "\"Currency\" = 'EUR'");
            table.HasCheckConstraint(
                "CK_Payments_Provider",
                """
                "Provider" IN ('Stripe', 'Redsys', 'PayPal')
                """);
            table.HasCheckConstraint(
                "CK_Payments_CheckoutSession",
                """
                ("CheckoutUrl" IS NULL AND "CheckoutExpiresAt" IS NULL)
                OR ("Provider" = 'Stripe' AND "StripeCheckoutSessionId" IS NOT NULL AND "CheckoutUrl" IS NOT NULL AND "CheckoutExpiresAt" IS NOT NULL AND "RedsysOrderNumber" IS NULL)
                OR ("Provider" = 'Redsys' AND "ExternalOperationId" IS NOT NULL AND "RedsysOrderNumber" IS NOT NULL AND "CheckoutUrl" IS NOT NULL AND "CheckoutExpiresAt" IS NOT NULL AND "StripeCheckoutSessionId" IS NULL AND "StripePaymentIntentId" IS NULL)
                OR ("Provider" = 'PayPal' AND "ExternalOperationId" IS NOT NULL AND "RedsysOrderNumber" IS NULL AND "CheckoutUrl" IS NOT NULL AND "CheckoutExpiresAt" IS NOT NULL AND "StripeCheckoutSessionId" IS NULL AND "StripePaymentIntentId" IS NULL)
                """);
            table.HasCheckConstraint(
                "CK_Payments_PayPalCreate",
                """
                ("ExternalCreateOutcome" IS NULL AND "ExternalRequestId" IS NULL AND "ExternalRequestFingerprint" IS NULL AND "ExternalCreateAttemptedAt" IS NULL)
                OR ("Provider" = 'PayPal' AND "ExternalCreateOutcome" IN ('Attempted', 'Uncertain') AND "ExternalOperationId" IS NULL AND "ExternalRequestId" IS NOT NULL AND "ExternalRequestFingerprint" IS NOT NULL AND "ExternalCreateAttemptedAt" IS NOT NULL)
                OR ("Provider" = 'PayPal' AND "ExternalCreateOutcome" = 'Identified' AND "ExternalOperationId" IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "CK_Payments_ProviderReferences",
                """
                ("StripeCheckoutSessionId" IS NULL OR "Provider" = 'Stripe')
                AND ("StripePaymentIntentId" IS NULL OR "Provider" = 'Stripe')
                AND ("RedsysOrderNumber" IS NULL OR ("Provider" = 'Redsys' AND "RedsysOrderNumber" ~ '^[0-9]{4,12}$'))
                """);
            table.HasCheckConstraint(
                "CK_Payments_StatusTimes",
                """
                ("Status" = 'Pending' AND "SucceededAt" IS NULL AND "FailedAt" IS NULL AND "CancelledAt" IS NULL AND "RefundedAt" IS NULL)
                OR ("Status" = 'Succeeded' AND "SucceededAt" IS NOT NULL AND "FailedAt" IS NULL AND "CancelledAt" IS NULL AND "RefundedAt" IS NULL)
                OR ("Status" = 'Failed' AND "FailedAt" IS NOT NULL AND "SucceededAt" IS NULL AND "CancelledAt" IS NULL AND "RefundedAt" IS NULL)
                OR ("Status" = 'Cancelled' AND "CancelledAt" IS NOT NULL AND "SucceededAt" IS NULL AND "FailedAt" IS NULL AND "RefundedAt" IS NULL)
                OR ("Status" = 'Refunded' AND "SucceededAt" IS NOT NULL AND "RefundedAt" IS NOT NULL AND "FailedAt" IS NULL AND "CancelledAt" IS NULL)
                """);
        });

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(payment => payment.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(payment => payment.Provider)
            .HasColumnType($"character varying({Payment.ProviderMaxLength})")
            .HasMaxLength(Payment.ProviderMaxLength)
            .HasConversion<string>()
            .HasDefaultValue(PaymentProvider.Stripe)
            .IsRequired();

        builder.Property(payment => payment.Status)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(payment => payment.AmountCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(payment => payment.Currency)
            .HasColumnType("character varying(3)")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(payment => payment.ExternalOperationId)
            .HasColumnType($"character varying({Payment.ExternalIdMaxLength})")
            .HasMaxLength(Payment.ExternalIdMaxLength);

        builder.Property(payment => payment.ExternalCaptureId)
            .HasColumnType($"character varying({Payment.ExternalIdMaxLength})")
            .HasMaxLength(Payment.ExternalIdMaxLength);

        builder.Property(payment => payment.RedsysOrderNumber)
            .HasColumnType($"character varying({Payment.RedsysOrderNumberMaxLength})")
            .HasMaxLength(Payment.RedsysOrderNumberMaxLength);

        builder.Property(payment => payment.StripeCheckoutSessionId)
            .HasColumnType($"character varying({Payment.StripeIdMaxLength})")
            .HasMaxLength(Payment.StripeIdMaxLength);

        builder.Property(payment => payment.CheckoutUrl)
            .HasColumnType($"character varying({Payment.CheckoutUrlMaxLength})")
            .HasMaxLength(Payment.CheckoutUrlMaxLength);

        builder.Property(payment => payment.CheckoutExpiresAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.StripePaymentIntentId)
            .HasColumnType($"character varying({Payment.StripeIdMaxLength})")
            .HasMaxLength(Payment.StripeIdMaxLength);

        builder.Property(payment => payment.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(payment => payment.SucceededAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.FailedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.CancelledAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.RefundedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.ExternalRequestId)
            .HasColumnType($"character varying({Payment.ExternalRequestIdMaxLength})")
            .HasMaxLength(Payment.ExternalRequestIdMaxLength);

        builder.Property(payment => payment.ExternalRequestFingerprint)
            .HasColumnType($"character varying({Payment.ExternalRequestFingerprintMaxLength})")
            .HasMaxLength(Payment.ExternalRequestFingerprintMaxLength);

        builder.Property(payment => payment.ExternalCreateAttemptedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(payment => payment.ExternalCreateOutcome)
            .HasColumnType("character varying(16)")
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.HasIndex(payment => payment.OrderId)
            .HasDatabaseName("IX_Payments_OrderId");

        builder.HasIndex(payment => payment.OrderId, "IX_Payments_OnePending")
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        builder.HasIndex(payment => new { payment.Provider, payment.ExternalOperationId })
            .IsUnique()
            .HasFilter("\"ExternalOperationId\" IS NOT NULL")
            .HasDatabaseName("IX_Payments_Provider_ExternalOperationId");

        builder.HasIndex(payment => payment.RedsysOrderNumber)
            .IsUnique()
            .HasFilter("\"RedsysOrderNumber\" IS NOT NULL")
            .HasDatabaseName("IX_Payments_RedsysOrderNumber");

        builder.HasIndex(payment => payment.StripeCheckoutSessionId)
            .IsUnique()
            .HasFilter("\"StripeCheckoutSessionId\" IS NOT NULL");

        builder.HasIndex(payment => payment.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("\"StripePaymentIntentId\" IS NOT NULL");

        builder.HasMany(payment => payment.Events)
            .WithOne(paymentEvent => paymentEvent.Payment)
            .HasForeignKey(paymentEvent => paymentEvent.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(payment => payment.Events)
            .HasField("events")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
