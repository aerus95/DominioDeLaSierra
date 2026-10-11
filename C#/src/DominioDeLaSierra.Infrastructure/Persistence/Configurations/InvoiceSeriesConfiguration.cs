using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DominioDeLaSierra.Infrastructure.Persistence.Configurations;

public sealed class InvoiceSeriesConfiguration : IEntityTypeConfiguration<InvoiceSeries>
{
    public void Configure(EntityTypeBuilder<InvoiceSeries> builder)
    {
        builder.ToTable("InvoiceSeries", table =>
        {
            table.HasCheckConstraint(
                "CK_InvoiceSeries_Number",
                $"""
                "NextNumber" >= 1
                AND "NextNumber" <= {Invoice.MaxNumber + 1}
                """);
        });

        builder.HasKey(series => series.Code);

        builder.Property(series => series.Code)
            .HasColumnType($"character varying({InvoiceSeries.CodeMaxLength})")
            .HasMaxLength(InvoiceSeries.CodeMaxLength)
            .IsRequired();

        builder.Property(series => series.NextNumber)
            .HasColumnType("integer")
            .IsRequired();
    }
}
