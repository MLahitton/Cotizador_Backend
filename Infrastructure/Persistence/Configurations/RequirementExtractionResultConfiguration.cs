using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExtractionResultConfiguration
    : IEntityTypeConfiguration<RequirementExtractionResult>
{
    public void Configure(
        EntityTypeBuilder<RequirementExtractionResult> builder)
    {
        builder.ToTable(
            "requirement_extraction_results",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_extraction_results_counts",
                    "\"item_count\" >= 0 " +
                    "AND \"items_requiring_review\" >= 0 " +
                    "AND \"items_requiring_review\" <= \"item_count\" " +
                    "AND \"issue_count\" >= 0 " +
                    "AND \"conflict_count\" >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_requirement_extraction_results_duration",
                    "\"duration_ms\" >= 0");
            });

        builder.HasKey(result => result.Id);

        builder.Property(result => result.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(result => result.RequirementProcessingAttemptId)
            .HasColumnName("requirement_processing_attempt_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(result => result.SchemaVersion)
            .HasColumnName("schema_version")
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(result => result.Provider)
            .HasColumnName("provider")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(result => result.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(result => result.ItemCount)
            .HasColumnName("item_count")
            .IsRequired();

        builder.Property(result => result.ItemsRequiringReview)
            .HasColumnName("items_requiring_review")
            .IsRequired();

        builder.Property(result => result.IssueCount)
            .HasColumnName("issue_count")
            .IsRequired();

        builder.Property(result => result.ConflictCount)
            .HasColumnName("conflict_count")
            .IsRequired();

        builder.Property(result => result.ProcessingMethod)
            .HasColumnName("processing_method")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(result => result.DurationMs)
            .HasColumnName("duration_ms")
            .IsRequired();

        builder.Property(result => result.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(result => result.ProcessingAttempt)
            .WithOne(attempt => attempt.ExtractionResult)
            .HasForeignKey<RequirementExtractionResult>(
                result => result.RequirementProcessingAttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(result => result.Items)
            .WithOne(item => item.ExtractionResult)
            .HasForeignKey(item => item.RequirementExtractionResultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(result => result.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(result => result.RequirementProcessingAttemptId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_extraction_results_processing_attempt_id");
    }
}
