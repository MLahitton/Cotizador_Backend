using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PreQuoteConfiguration
    : IEntityTypeConfiguration<PreQuote>
{
    public void Configure(EntityTypeBuilder<PreQuote> builder)
    {
        builder.ToTable("pre_quotes", "core");

        builder.HasKey(preQuote => preQuote.Id);

        builder.Property(preQuote => preQuote.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(preQuote => preQuote.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        builder.Property(preQuote => preQuote.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(preQuote => preQuote.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(preQuote => preQuote.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(preQuote => preQuote.Project)
            .WithMany()
            .HasForeignKey(preQuote => preQuote.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(preQuote => preQuote.CreatedByUser)
            .WithMany()
            .HasForeignKey(preQuote => preQuote.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(preQuote => preQuote.ProjectId)
            .HasDatabaseName("ix_pre_quotes_project_id");

        builder.HasIndex(preQuote => preQuote.CreatedByUserId)
            .HasDatabaseName("ix_pre_quotes_created_by_user_id");

        builder.HasIndex(preQuote => preQuote.UpdatedAtUtc)
            .HasDatabaseName("ix_pre_quotes_updated_at_utc");
    }
}
