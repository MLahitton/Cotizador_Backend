using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementProcessingAttemptConfiguration
    : IEntityTypeConfiguration<RequirementProcessingAttempt>
{
    public void Configure(
        EntityTypeBuilder<RequirementProcessingAttempt> builder)
    {
        builder.ToTable(
            "requirement_processing_attempts",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_processing_attempts_lifecycle",
                "((\"processing_state\" = 'Pending' " +
                "AND \"started_at_utc\" IS NULL " +
                "AND \"outcome\" IS NULL " +
                "AND \"completed_at_utc\" IS NULL " +
                "AND \"error_code\" IS NULL) " +
                "OR (\"processing_state\" = 'Processing' " +
                "AND \"started_at_utc\" IS NOT NULL " +
                "AND \"started_at_utc\" >= \"created_at_utc\" " +
                "AND \"outcome\" IS NULL " +
                "AND \"completed_at_utc\" IS NULL " +
                "AND \"error_code\" IS NULL) " +
                "OR (\"processing_state\" = 'Finished' " +
                "AND \"started_at_utc\" IS NOT NULL " +
                "AND \"started_at_utc\" >= \"created_at_utc\" " +
                "AND \"completed_at_utc\" IS NOT NULL " +
                "AND \"completed_at_utc\" >= \"started_at_utc\" " +
                "AND ((\"outcome\" IN ('Completed', 'RequiresReview') " +
                "AND \"error_code\" IS NULL) " +
                "OR (\"outcome\" = 'Failed' " +
                "AND \"error_code\" IS NOT NULL " +
                "AND \"error_code\" <> '') " +
                "OR (\"outcome\" = 'Cancelled' " +
                "AND \"error_code\" IS NULL))))"));

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(attempt => attempt.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(attempt => attempt.RequestedByUserId)
            .HasColumnName("requested_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(attempt => attempt.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(attempt => attempt.ProcessingState)
            .HasColumnName("processing_state")
            .HasConversion<string>()
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(attempt => attempt.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsRequired(false);

        builder.Property(attempt => attempt.ErrorCode)
            .HasColumnName("error_code")
            .HasColumnType("varchar(64)")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(attempt => attempt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(attempt => attempt.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(attempt => attempt.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasOne(attempt => attempt.RequestedByUser)
            .WithMany()
            .HasForeignKey(attempt => attempt.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attempt => attempt.RequirementId)
            .HasDatabaseName(
                "ix_requirement_processing_attempts_requirement_id");

        builder.HasIndex(attempt => attempt.RequirementId)
            .IsUnique()
            .HasFilter("\"processing_state\" IN ('Pending', 'Processing')")
            .HasDatabaseName(
                "ux_requirement_processing_attempts_active_requirement_id");

        builder.HasIndex(attempt => attempt.RequestedByUserId)
            .HasDatabaseName(
                "ix_requirement_processing_attempts_requested_by_user_id");

        builder.HasIndex(attempt => new
            {
                attempt.ProcessingState,
                attempt.CreatedAtUtc,
                attempt.Id
            })
            .HasDatabaseName(
                "ix_requirement_processing_attempts_state_created_id");

        builder.HasIndex(attempt => attempt.CorrelationId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_processing_attempts_correlation_id");
    }
}
