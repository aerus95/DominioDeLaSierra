using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint(
                "CK_Orders_Amounts",
                """
                "ProductSubtotalCents" >= 0
                AND "ProductTaxableBaseCents" >= 0
                AND "ProductVatCents" >= 0
                AND "ShippingCents" >= 0
                AND "TotalCents" >= 0
                AND "ProductTaxableBaseCents" + "ProductVatCents" = "ProductSubtotalCents"
                AND "TotalCents" = "ProductSubtotalCents" + "ShippingCents"
                """);
            table.HasCheckConstraint(
                "CK_Orders_ShippingVat",
                """
                ("ShippingVatRate" IS NULL AND "ShippingTaxableBaseCents" IS NULL AND "ShippingVatCents" IS NULL)
                OR ("ShippingVatRate" IS NOT NULL AND "ShippingTaxableBaseCents" IS NOT NULL AND "ShippingVatCents" IS NOT NULL
                    AND "ShippingTaxableBaseCents" >= 0 AND "ShippingVatCents" >= 0
                    AND "ShippingTaxableBaseCents" + "ShippingVatCents" = "ShippingCents")
                """);
            table.HasCheckConstraint(
                "CK_Orders_StatusTimes",
                """
                ("Status" = 'PendingPayment' AND "PaidAt" IS NULL AND "CancelledAt" IS NULL AND "ExpiredAt" IS NULL)
                OR ("Status" = 'Paid' AND "PaidAt" IS NOT NULL AND "CancelledAt" IS NULL AND "ExpiredAt" IS NULL)
                OR ("Status" = 'Cancelled' AND "CancelledAt" IS NOT NULL AND "PaidAt" IS NULL AND "ExpiredAt" IS NULL)
                OR ("Status" = 'Expired' AND "ExpiredAt" IS NOT NULL AND "PaidAt" IS NULL AND "CancelledAt" IS NULL)
                """);
            table.HasCheckConstraint("CK_Orders_Currency", "\"Currency\" = 'EUR'");
            table.HasCheckConstraint("CK_Orders_Country", "\"CountryCode\" = 'ES'");
            table.HasCheckConstraint("CK_Orders_Reservation", "\"ReservationExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint(
                "CK_Orders_Fulfillment",
                """
                "FulfillmentStatus" IN ('Unfulfilled', 'Preparing', 'Prepared', 'Shipped')
                AND (
                    ("Status" = 'Paid')
                    OR ("FulfillmentStatus" = 'Unfulfilled'
                        AND "Carrier" IS NULL
                        AND "TrackingNumber" IS NULL
                        AND "FulfillmentUpdatedAt" IS NULL
                        AND "FulfillmentUpdatedByUserId" IS NULL)
                )
                AND (
                    ("FulfillmentStatus" = 'Unfulfilled'
                        AND "FulfillmentUpdatedAt" IS NULL
                        AND "FulfillmentUpdatedByUserId" IS NULL)
                    OR ("FulfillmentStatus" <> 'Unfulfilled'
                        AND "FulfillmentUpdatedAt" IS NOT NULL
                        AND "FulfillmentUpdatedByUserId" IS NOT NULL)
                )
                """);
        });

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(order => order.Number)
            .HasColumnType($"character varying({Order.NumberMaxLength})")
            .HasMaxLength(Order.NumberMaxLength)
            .IsRequired();

        builder.Property(order => order.Status)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(order => order.CustomerName)
            .HasColumnType($"character varying({Order.CustomerNameMaxLength})")
            .HasMaxLength(Order.CustomerNameMaxLength)
            .IsRequired();

        builder.Property(order => order.Email)
            .HasColumnType($"character varying({Order.EmailMaxLength})")
            .HasMaxLength(Order.EmailMaxLength)
            .IsRequired();

        builder.Property(order => order.Phone)
            .HasColumnType($"character varying({Order.PhoneMaxLength})")
            .HasMaxLength(Order.PhoneMaxLength)
            .IsRequired();

        builder.Property(order => order.AddressLine)
            .HasColumnType($"character varying({Order.AddressLineMaxLength})")
            .HasMaxLength(Order.AddressLineMaxLength)
            .IsRequired();

        builder.Property(order => order.PostalCode)
            .HasColumnType($"character varying({Order.PostalCodeMaxLength})")
            .HasMaxLength(Order.PostalCodeMaxLength)
            .IsRequired();

        builder.Property(order => order.City)
            .HasColumnType($"character varying({Order.CityMaxLength})")
            .HasMaxLength(Order.CityMaxLength)
            .IsRequired();

        builder.Property(order => order.Province)
            .HasColumnType($"character varying({Order.ProvinceMaxLength})")
            .HasMaxLength(Order.ProvinceMaxLength)
            .IsRequired();

        builder.Property(order => order.CountryCode)
            .HasColumnType("character varying(2)")
            .HasMaxLength(2)
            .IsRequired();

        builder.Property(order => order.DeliveryNotes)
            .HasColumnType($"character varying({Order.DeliveryNotesMaxLength})")
            .HasMaxLength(Order.DeliveryNotesMaxLength);

        builder.Property(order => order.ProductSubtotalCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(order => order.ProductTaxableBaseCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(order => order.ProductVatCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(order => order.ShippingCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(order => order.ShippingVatRate)
            .HasColumnType("numeric(5,2)");

        builder.Property(order => order.ShippingTaxableBaseCents)
            .HasColumnType("bigint");

        builder.Property(order => order.ShippingVatCents)
            .HasColumnType("bigint");

        builder.Property(order => order.TotalCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(order => order.Currency)
            .HasColumnType("character varying(3)")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(order => order.IdempotencyKey)
            .HasColumnType($"character varying({Order.IdempotencyKeyMaxLength})")
            .HasMaxLength(Order.IdempotencyKeyMaxLength)
            .IsRequired();

        builder.Property(order => order.CheckoutAccessTokenHash)
            .HasColumnType($"character varying({CheckoutAccessToken.HashLength})")
            .HasMaxLength(CheckoutAccessToken.HashLength)
            .IsRequired();

        builder.Property(order => order.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(order => order.ReservationExpiresAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(order => order.PaidAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(order => order.CancelledAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(order => order.ExpiredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(order => order.FulfillmentStatus)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .HasDefaultValue(FulfillmentStatus.Unfulfilled)
            .IsRequired();

        builder.Property(order => order.Carrier)
            .HasColumnType($"character varying({Order.CarrierMaxLength})")
            .HasMaxLength(Order.CarrierMaxLength);

        builder.Property(order => order.TrackingNumber)
            .HasColumnType($"character varying({Order.TrackingNumberMaxLength})")
            .HasMaxLength(Order.TrackingNumberMaxLength);

        builder.Property(order => order.FulfillmentUpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(order => order.FulfillmentUpdatedByUserId)
            .HasColumnType("uuid");

        builder.HasIndex(order => order.Number)
            .IsUnique();

        builder.HasIndex(order => order.IdempotencyKey)
            .IsUnique();

        builder.HasIndex(order => order.CheckoutAccessTokenHash)
            .IsUnique();

        builder.HasIndex(order => order.Email);

        builder.HasIndex(order => order.CreatedAt);

        builder.HasIndex(order => order.ReservationExpiresAt)
            .HasFilter("\"Status\" = 'PendingPayment'");

        builder.HasMany(order => order.Items)
            .WithOne(item => item.Order)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Items)
            .HasField("items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(order => order.Payments)
            .WithOne(payment => payment.Order)
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Payments)
            .HasField("payments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(order => order.FulfillmentTransitions)
            .WithOne(transition => transition.Order)
            .HasForeignKey(transition => transition.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.FulfillmentTransitions)
            .HasField("fulfillmentTransitions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
