using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderItems_Amounts",
                $"""
                "Quantity" > 0
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

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.OrderId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.ProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.Name)
            .HasColumnType($"character varying({OrderItem.NameMaxLength})")
            .HasMaxLength(OrderItem.NameMaxLength)
            .IsRequired();

        builder.Property(item => item.Reference)
            .HasColumnType($"character varying({OrderItem.ReferenceMaxLength})")
            .HasMaxLength(OrderItem.ReferenceMaxLength)
            .IsRequired();

        builder.Property(item => item.Kind)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(item => item.Quantity)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(item => item.UnitPriceCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(item => item.VatRate)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(item => item.LineTotalCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(item => item.TaxableBaseCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(item => item.VatCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.HasIndex(item => new { item.OrderId, item.ProductId })
            .IsUnique();

        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.Components)
            .WithOne(component => component.OrderItem)
            .HasForeignKey(component => component.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(item => item.Components)
            .HasField("components")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
