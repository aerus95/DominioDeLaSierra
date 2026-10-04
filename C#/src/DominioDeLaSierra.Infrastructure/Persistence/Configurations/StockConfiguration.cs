using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stocks", table =>
        {
            table.HasCheckConstraint("CK_Stocks_Quantity", "\"Quantity\" >= 0");
        });

        builder.HasKey(stock => stock.Id);

        builder.Property(stock => stock.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(stock => stock.WarehouseId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(stock => stock.ProductId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(stock => stock.Quantity)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(stock => stock.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(stock => new { stock.WarehouseId, stock.ProductId })
            .IsUnique();

        builder.HasIndex(stock => stock.ProductId);

        builder.HasOne(stock => stock.Warehouse)
            .WithMany(warehouse => warehouse.Stocks)
            .HasForeignKey(stock => stock.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(stock => stock.Product)
            .WithMany()
            .HasForeignKey(stock => stock.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
