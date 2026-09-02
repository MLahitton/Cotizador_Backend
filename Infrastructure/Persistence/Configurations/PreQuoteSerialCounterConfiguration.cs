using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PreQuoteSerialCounterConfiguration
    : IEntityTypeConfiguration<PreQuoteSerialCounter>
{
    public void Configure(EntityTypeBuilder<PreQuoteSerialCounter> builder)
    {
        builder.ToTable(
            "pre_quote_serial_counters",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_pre_quote_serial_counters_next_sequence",
                "\"next_sequence\" > 0"));

        builder.HasKey(counter => counter.Year);

        builder.Property(counter => counter.Year)
            .HasColumnName("year")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(counter => counter.NextSequence)
            .HasColumnName("next_sequence")
            .IsRequired();
    }
}