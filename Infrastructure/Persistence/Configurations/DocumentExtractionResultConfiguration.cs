using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class DocumentExtractionResultConfiguration
    : IEntityTypeConfiguration<DocumentExtractionResult>
{
    public void Configure(
        EntityTypeBuilder<DocumentExtractionResult> builder)
    {
        builder.ToTable(
            "document_extraction_results",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_document_extraction_results_page_count_by_classification",
                    "(\"classification\" = 'PdfText' AND \"page_count\" >= 1) " +
                    "OR (\"classification\" = 'PdfScanned' AND \"page_count\" >= 1) " +
                    "OR (\"classification\" = 'PdfMixed' AND \"page_count\" >= 1) " +
                    "OR (\"classification\" = 'Xlsx' AND \"page_count\" = 0)");

                tableBuilder.HasCheckConstraint(
                    "ck_document_extraction_results_duration_ms_non_negative",
                    "\"duration_ms\" >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_document_extraction_results_classification_ocr",
                    "((\"classification\" = 'PdfText' " +
                    "AND \"requires_ocr\" = false) " +
                    "OR (\"classification\" = 'PdfScanned' " +
                    "AND \"requires_ocr\" = true) " +
                    "OR (\"classification\" = 'PdfMixed' " +
                    "AND \"requires_ocr\" = true) " +
                    "OR (\"classification\" = 'Xlsx' " +
                    "AND \"requires_ocr\" = false))");
            });

        builder.HasKey(result => result.Id);

        builder.Property(result => result.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(result => result.DocumentProcessingAttemptId)
            .HasColumnName("document_processing_attempt_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(result => result.SchemaVersion)
            .HasColumnName("schema_version")
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(result => result.Classification)
            .HasColumnName("classification")
            .HasConversion<string>()
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(result => result.RequiresOcr)
            .HasColumnName("requires_ocr")
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(result => result.PageCount)
            .HasColumnName("page_count")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(result => result.ProcessingMethod)
            .HasColumnName("processing_method")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(result => result.DurationMs)
            .HasColumnName("duration_ms")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(result => result.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(result => result.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(result => result.ProcessingAttempt)
            .WithOne(attempt => attempt.ExtractionResult)
            .HasForeignKey<DocumentExtractionResult>(
                result => result.DocumentProcessingAttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(result => result.DocumentProcessingAttemptId)
            .IsUnique()
            .HasDatabaseName(
                "ux_document_extraction_results_processing_attempt_id");
    }
}
