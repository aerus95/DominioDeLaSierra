using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> builder)
    {
        builder.ToTable("AdminUsers");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(user => user.Username)
            .HasColumnType("character varying(80)")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasColumnType("character varying(500)")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(user => user.DisplayName)
            .HasColumnType("character varying(150)")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(user => user.Role)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(user => user.Active)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.LastLoginAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(user => user.Username)
            .IsUnique();
    }
}
