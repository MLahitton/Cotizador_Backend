using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class DocumentProcessingAttemptConfiguration
    : IEntityTypeConfiguration<DocumentProcessingAttempt>
{
    public void Configure(
        EntityTypeBuilder<DocumentProcessingAttempt> builder)
    {
        builder.ToTable(
            "document_processing_attempts",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_document_processing_attempts_final_state",
                "((\"outcome\" IS NULL " +
                "AND \"completed_at_utc\" IS NULL " +
                "AND \"error_code\" IS NULL) " +
                "OR (\"outcome\" IS NOT NULL " +
                "AND \"outcome\" IN ('Completed', 'RequiresReview') " +
                "AND \"completed_at_utc\" IS NOT NULL " +
                "AND \"error_code\" IS NULL) " +
                "OR (\"outcome\" IS NOT NULL " +
                "AND \"outcome\" = 'Failed' " +
                "AND \"completed_at_utc\" IS NOT NULL " +
                "AND \"error_code\" IS NOT NULL " +
                "AND \"error_code\" <> ''))"));

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(attempt => attempt.PreQuoteDocumentId)
            .HasColumnName("pre_quote_document_id")
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

        builder.Property(attempt => attempt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(attempt => attempt.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

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

        builder.HasOne(attempt => attempt.PreQuoteDocument)
            .WithMany()
            .HasForeignKey(attempt => attempt.PreQuoteDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(attempt => attempt.RequestedByUser)
            .WithMany()
            .HasForeignKey(attempt => attempt.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attempt => attempt.PreQuoteDocumentId)
            .HasDatabaseName(
                "ix_document_processing_attempts_pre_quote_document_id");

        builder.HasIndex(attempt => attempt.RequestedByUserId)
            .HasDatabaseName(
                "ix_document_processing_attempts_requested_by_user_id");

        builder.HasIndex(attempt => attempt.CreatedAtUtc)
            .HasDatabaseName(
                "ix_document_processing_attempts_created_at_utc");

        builder.HasIndex(attempt => attempt.CorrelationId)
            .IsUnique()
            .HasDatabaseName(
                "ux_document_processing_attempts_correlation_id");
    }
}
