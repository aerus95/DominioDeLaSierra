using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Quantity", "\"Quantity\" <> 0");
        });

        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.Id)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(movement => movement.StockId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(movement => movement.Type)
            .HasColumnType("character varying(32)")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(movement => movement.Quantity)
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(movement => movement.OccurredAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(movement => movement.Note)
            .HasColumnType($"character varying({StockMovement.NoteMaxLength})")
            .HasMaxLength(StockMovement.NoteMaxLength);

        builder.HasIndex(movement => new { movement.StockId, movement.OccurredAt });

        builder.HasOne(movement => movement.Stock)
            .WithMany(stock => stock.Movements)
            .HasForeignKey(movement => movement.StockId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
