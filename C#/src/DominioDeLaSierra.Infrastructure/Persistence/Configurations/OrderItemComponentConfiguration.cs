using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class OrderItemComponentConfiguration : IEntityTypeConfiguration<OrderItemComponent>
{
    public void Configure(EntityTypeBuilder<OrderItemComponent> builder)
    {
        builder.ToTable("OrderItemComponents", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderItemComponents_QuantityPerPack",
                $"\"QuantityPerPack\" > 0 AND \"QuantityPerPack\" <= {OrderAmounts.MaxLineQuantity}");
        });

        builder.HasKey(component => component.Id);

        builder.Property(component => component.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.OrderItemId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.ComponentProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.Name)
            .HasColumnType($"character varying({OrderItem.NameMaxLength})")
            .HasMaxLength(OrderItem.NameMaxLength)
            .IsRequired();

        builder.Property(component => component.Reference)
            .HasColumnType($"character varying({OrderItem.ReferenceMaxLength})")
            .HasMaxLength(OrderItem.ReferenceMaxLength)
            .IsRequired();

        builder.Property(component => component.QuantityPerPack)
            .HasColumnType("integer")
            .IsRequired();

        builder.HasIndex(component => new { component.OrderItemId, component.ComponentProductId })
            .IsUnique();

        builder.HasOne(component => component.ComponentProduct)
            .WithMany()
            .HasForeignKey(component => component.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
