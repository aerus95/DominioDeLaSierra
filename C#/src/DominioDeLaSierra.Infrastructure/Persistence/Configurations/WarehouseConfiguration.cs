using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public static readonly Guid PrincipalId = Guid.Parse("8f4e2c10-6b3a-4d77-9c2e-1a5b7d3e6f90");
    public static readonly DateTimeOffset PrincipalCreatedAt = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(warehouse => warehouse.Id);

        builder.Property(warehouse => warehouse.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(warehouse => warehouse.Code)
            .HasColumnType($"character varying({Warehouse.CodeMaxLength})")
            .HasMaxLength(Warehouse.CodeMaxLength)
            .IsRequired();

        builder.Property(warehouse => warehouse.Name)
            .HasColumnType($"character varying({Warehouse.NameMaxLength})")
            .HasMaxLength(Warehouse.NameMaxLength)
            .IsRequired();

        builder.Property(warehouse => warehouse.Active)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(warehouse => warehouse.IsDefault)
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(warehouse => warehouse.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique();

        builder.HasIndex(warehouse => warehouse.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = TRUE");

        builder.HasData(new Warehouse(
            PrincipalId,
            InventoryLimits.PrincipalWarehouseCode,
            InventoryLimits.PrincipalWarehouseName,
            true,
            true,
            PrincipalCreatedAt));
    }
}
