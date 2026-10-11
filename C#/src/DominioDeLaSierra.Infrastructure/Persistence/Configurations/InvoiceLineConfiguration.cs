using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines", table =>
        {
            table.HasCheckConstraint(
                "CK_InvoiceLines_Amounts",
                $"""
                "Position" > 0
                AND "Quantity" > 0
                AND "Quantity" <= {OrderAmounts.MaxLineQuantity}
                AND "UnitPriceCents" >= 0
                AND "LineTotalCents" >= 0
                AND "TaxableBaseCents" >= 0
                AND "VatCents" >= 0
                AND "LineTotalCents" = "UnitPriceCents" * "Quantity"
                AND "TaxableBaseCents" + "VatCents" = "LineTotalCents"
                AND "VatRate" >= 0
                AND "VatRate" <= 999.99
                """);
        });

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(line => line.InvoiceId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasIndex(line => new { line.InvoiceId, line.Position })
            .IsUnique()
            .HasDatabaseName("IX_InvoiceLines_InvoiceId_Position");

        builder.Property(line => line.Position)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(line => line.Name)
            .HasColumnType($"character varying({OrderItem.NameMaxLength})")
            .HasMaxLength(OrderItem.NameMaxLength)
            .IsRequired();

        builder.Property(line => line.Reference)
            .HasColumnType($"character varying({OrderItem.ReferenceMaxLength})")
            .HasMaxLength(OrderItem.ReferenceMaxLength)
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(line => line.UnitPriceCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(line => line.VatRate)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(line => line.LineTotalCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(line => line.TaxableBaseCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(line => line.VatCents)
            .HasColumnType("bigint")
            .IsRequired();
    }
}
