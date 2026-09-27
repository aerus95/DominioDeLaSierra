using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(product => product.Reference)
            .HasColumnType("character varying(80)")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(product => product.Name)
            .HasColumnType("character varying(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Slug)
            .HasColumnType("character varying(220)")
            .HasMaxLength(220)
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(product => product.CategoryId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(product => product.Price)
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(product => product.VatRate)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(product => product.Active)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(product => product.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(product => product.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(product => product.Reference)
            .IsUnique();

        builder.HasIndex(product => product.Slug)
            .IsUnique();

        builder.HasIndex(product => product.CategoryId);

        builder.HasIndex(product => product.Active);

        builder.HasOne(product => product.Category)
            .WithMany(category => category.Products)
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
