using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", table =>
        {
            table.HasCheckConstraint(
                "CK_Invoices_Amounts",
                """
                "ProductSubtotalCents" >= 0
                AND "ProductTaxableBaseCents" >= 0
                AND "ProductVatCents" >= 0
                AND "ShippingCents" >= 0
                AND "TotalCents" >= 0
                AND "ProductTaxableBaseCents" + "ProductVatCents" = "ProductSubtotalCents"
                AND "TotalCents" = "ProductSubtotalCents" + "ShippingCents"
                AND (
                    (
                        "Fictional" = TRUE
                        AND "ShippingVatRate" IS NULL
                        AND "ShippingTaxableBaseCents" IS NULL
                        AND "ShippingVatCents" IS NULL
                    )
                    OR (
                        "ShippingVatRate" IS NOT NULL
                        AND "ShippingTaxableBaseCents" IS NOT NULL
                        AND "ShippingVatCents" IS NOT NULL
                        AND "ShippingTaxableBaseCents" >= 0
                        AND "ShippingVatCents" >= 0
                        AND "ShippingTaxableBaseCents" + "ShippingVatCents" = "ShippingCents"
                        AND "ShippingVatRate" >= 0
                        AND "ShippingVatRate" <= 999.99
                    )
                )
                """);
            table.HasCheckConstraint(
                "CK_Invoices_DocumentKind",
                """
                (
                    "Fictional" = TRUE
                    AND "FiscalForm" = 'NotApplicable'
                    AND "SellerTaxId" = 'FICTICIO-TEST'
                )
                OR (
                    "Fictional" = FALSE
                    AND "FiscalForm" IN ('Complete', 'Simplified')
                    AND "BuyerTaxId" IS NOT NULL
                    AND "ShippingVatRate" IS NOT NULL
                )
                """);
            table.HasCheckConstraint("CK_Invoices_Currency", "\"Currency\" = 'EUR'");
            table.HasCheckConstraint(
                "CK_Invoices_Number",
                $"""
                "Number" >= 1
                AND "Number" <= {Invoice.MaxNumber}
                """);
        });

        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(invoice => invoice.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasIndex(invoice => invoice.OrderId)
            .IsUnique()
            .HasDatabaseName("IX_Invoices_OrderId");

        builder.Property(invoice => invoice.OrderNumber)
            .HasColumnType($"character varying({Order.NumberMaxLength})")
            .HasMaxLength(Order.NumberMaxLength)
            .IsRequired();

        builder.Property(invoice => invoice.Series)
            .HasColumnType($"character varying({InvoiceSeries.CodeMaxLength})")
            .HasMaxLength(InvoiceSeries.CodeMaxLength)
            .IsRequired();

        builder.Property(invoice => invoice.Number)
            .HasColumnType("integer")
            .IsRequired();

        builder.HasIndex(invoice => new { invoice.Series, invoice.Number })
            .IsUnique()
            .HasDatabaseName("IX_Invoices_Series_Number");

        builder.Property(invoice => invoice.FullNumber)
            .HasColumnType($"character varying({Invoice.FullNumberMaxLength})")
            .HasMaxLength(Invoice.FullNumberMaxLength)
            .IsRequired();

        builder.HasIndex(invoice => invoice.FullNumber)
            .IsUnique()
            .HasDatabaseName("IX_Invoices_FullNumber");

        builder.Property(invoice => invoice.IssuedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invoice => invoice.Fictional)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(invoice => invoice.FiscalForm)
            .HasConversion<string>()
            .HasColumnType("character varying(20)")
            .HasMaxLength(20)
            .IsRequired();

        MapParty(builder, invoice => invoice.SellerLegalName, FiscalIdentity.LegalNameMaxLength, "SellerLegalName");
        MapParty(builder, invoice => invoice.SellerTaxId, FiscalIdentity.TaxIdMaxLength, "SellerTaxId");
        MapParty(builder, invoice => invoice.SellerAddressLine, FiscalIdentity.AddressLineMaxLength, "SellerAddressLine");
        MapParty(builder, invoice => invoice.SellerPostalCode, FiscalIdentity.PostalCodeMaxLength, "SellerPostalCode");
        MapParty(builder, invoice => invoice.SellerCity, FiscalIdentity.CityMaxLength, "SellerCity");
        MapParty(builder, invoice => invoice.SellerProvince, FiscalIdentity.ProvinceMaxLength, "SellerProvince");
        MapParty(builder, invoice => invoice.SellerCountryCode, 2, "SellerCountryCode");
        MapParty(builder, invoice => invoice.BuyerName, FiscalIdentity.LegalNameMaxLength, "BuyerName");
        builder.Property(invoice => invoice.BuyerTaxId)
            .HasColumnName("BuyerTaxId")
            .HasColumnType($"character varying({FiscalIdentity.TaxIdMaxLength})")
            .HasMaxLength(FiscalIdentity.TaxIdMaxLength)
            .IsRequired(false);
        MapParty(builder, invoice => invoice.BuyerAddressLine, FiscalIdentity.AddressLineMaxLength, "BuyerAddressLine");
        MapParty(builder, invoice => invoice.BuyerPostalCode, FiscalIdentity.PostalCodeMaxLength, "BuyerPostalCode");
        MapParty(builder, invoice => invoice.BuyerCity, FiscalIdentity.CityMaxLength, "BuyerCity");
        MapParty(builder, invoice => invoice.BuyerProvince, FiscalIdentity.ProvinceMaxLength, "BuyerProvince");
        MapParty(builder, invoice => invoice.BuyerCountryCode, 2, "BuyerCountryCode");

        builder.Property(invoice => invoice.ProductSubtotalCents).HasColumnType("bigint").IsRequired();
        builder.Property(invoice => invoice.ProductTaxableBaseCents).HasColumnType("bigint").IsRequired();
        builder.Property(invoice => invoice.ProductVatCents).HasColumnType("bigint").IsRequired();
        builder.Property(invoice => invoice.ShippingCents).HasColumnType("bigint").IsRequired();
        builder.Property(invoice => invoice.ShippingVatRate).HasColumnType("numeric(5,2)").IsRequired(false);
        builder.Property(invoice => invoice.ShippingTaxableBaseCents).HasColumnType("bigint").IsRequired(false);
        builder.Property(invoice => invoice.ShippingVatCents).HasColumnType("bigint").IsRequired(false);
        builder.Property(invoice => invoice.TotalCents).HasColumnType("bigint").IsRequired();

        builder.Property(invoice => invoice.Currency)
            .HasColumnType("character varying(3)")
            .HasMaxLength(3)
            .IsRequired();

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(invoice => invoice.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(invoice => invoice.Lines)
            .WithOne(line => line.Invoice)
            .HasForeignKey(line => line.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Lines)
            .HasField("lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void MapParty(
        EntityTypeBuilder<Invoice> builder,
        System.Linq.Expressions.Expression<Func<Invoice, string>> property,
        int maxLength,
        string column)
    {
        builder.Property(property)
            .HasColumnName(column)
            .HasColumnType($"character varying({maxLength})")
            .HasMaxLength(maxLength)
            .IsRequired();
    }
}
