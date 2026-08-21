using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExtractedItemEvidenceConfiguration
    : IEntityTypeConfiguration<RequirementExtractedItemEvidence>
{
    public void Configure(
        EntityTypeBuilder<RequirementExtractedItemEvidence> builder)
    {
        builder.ToTable(
            "requirement_extracted_item_evidence",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_extracted_item_evidence_location",
                "((\"source_type\" IN ('Native','Ocr') " +
                "AND \"page_number\" IS NOT NULL " +
                "AND \"page_number\" > 0 " +
                "AND \"sheet_name\" IS NULL " +
                "AND \"cell_range\" IS NULL) " +
                "OR (\"source_type\" = 'Xlsx' " +
                "AND \"page_number\" IS NULL " +
                "AND \"sheet_name\" IS NOT NULL " +
                "AND \"cell_range\" IS NOT NULL " +
                "AND btrim(\"sheet_name\") <> '' " +
                "AND btrim(\"cell_range\") <> '')) " +
                "AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1))"));

        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(evidence => evidence.RequirementExtractedItemId)
            .HasColumnName("requirement_extracted_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(evidence => evidence.PageNumber)
            .HasColumnName("page_number")
            .IsRequired(false);

        builder.Property(evidence => evidence.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(evidence => evidence.Text)
            .HasColumnName("text")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(evidence => evidence.SheetName)
            .HasColumnName("sheet_name")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(evidence => evidence.CellRange)
            .HasColumnName("cell_range")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(evidence => evidence.SourceId)
            .HasColumnName("source_id")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(evidence => evidence.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired(false);

        builder.Property(evidence => evidence.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(evidence => evidence.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(evidence => evidence.RequirementExtractedItemId)
            .HasDatabaseName("ix_requirement_extracted_item_evidence_item_id");
    }
}
