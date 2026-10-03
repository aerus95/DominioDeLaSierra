using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class ProductComponentConfiguration : IEntityTypeConfiguration<ProductComponent>
{
    public void Configure(EntityTypeBuilder<ProductComponent> builder)
    {
        builder.ToTable("ProductComponents", table =>
        {
            table.HasCheckConstraint("CK_ProductComponents_Quantity", "\"Quantity\" > 0");
        });

        builder.HasKey(component => component.Id);

        builder.Property(component => component.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.PackProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.ComponentProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(component => component.Quantity)
            .IsRequired();

        builder.HasIndex(component => new { component.PackProductId, component.ComponentProductId })
            .IsUnique();

        builder.HasOne(component => component.PackProduct)
            .WithMany(product => product.Components)
            .HasForeignKey(component => component.PackProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(component => component.ComponentProduct)
            .WithMany(product => product.UsedInPacks)
            .HasForeignKey(component => component.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
