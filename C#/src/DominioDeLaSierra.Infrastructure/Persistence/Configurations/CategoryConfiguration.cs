using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnType("character varying(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(category => category.Slug)
            .HasColumnType("character varying(180)")
            .HasMaxLength(180)
            .IsRequired();

        builder.Property(category => category.ParentCategoryId)
            .HasColumnType("uuid");

        builder.Property(category => category.Active)
            .HasColumnType("boolean")
            .IsRequired();

        builder.HasIndex(category => category.Slug)
            .IsUnique();

        builder.HasIndex(category => category.ParentCategoryId);

        builder.HasOne(category => category.ParentCategory)
            .WithMany(category => category.Children)
            .HasForeignKey(category => category.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
