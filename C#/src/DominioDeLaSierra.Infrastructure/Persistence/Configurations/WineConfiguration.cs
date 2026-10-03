using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class WineConfiguration : IEntityTypeConfiguration<Wine>
{
    public void Configure(EntityTypeBuilder<Wine> builder)
    {
        builder.ToTable("Wines");

        builder.HasKey(wine => wine.ProductId);

        builder.Property(wine => wine.ProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(wine => wine.Vintage)
            .HasColumnType("character varying(20)")
            .HasMaxLength(20);

        builder.Property(wine => wine.Grape)
            .HasColumnType("character varying(150)")
            .HasMaxLength(150);

        builder.Property(wine => wine.AlcoholPercent)
            .HasColumnType("numeric(4,2)");

        builder.HasOne(wine => wine.Product)
            .WithOne(product => product.Wine)
            .HasForeignKey<Wine>(wine => wine.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
