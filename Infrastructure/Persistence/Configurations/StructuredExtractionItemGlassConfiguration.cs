using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class StructuredExtractionItemGlassDetectionConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemGlassDetection>
{
    public void Configure(
        EntityTypeBuilder<StructuredExtractionItemGlassDetection> b)
    {
        b.ToTable("structured_extraction_item_glass_detections", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_item_glass_detection_identity",
                "(\"normalized_code_snapshot\" IS NULL AND \"glass_type_id\" IS NULL) OR (\"normalized_code_snapshot\" IS NOT NULL AND \"glass_type_id\" IS NOT NULL)");
            t.HasCheckConstraint("ck_structured_item_glass_detection_scope",
                "\"assignment_scope\" IN ('Item', 'Section', 'General', 'Unassigned')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.StructuredExtractionItemId).HasColumnName("structured_extraction_item_id").HasColumnType("uuid");
        b.Property(x => x.GlassTypeId).HasColumnName("glass_type_id").HasColumnType("uuid");
        b.Property(x => x.RawSpecification).HasColumnName("raw_specification").HasMaxLength(500);
        b.Property(x => x.NormalizedCodeSnapshot).HasColumnName("normalized_code_snapshot").HasMaxLength(30);
        b.Property(x => x.AssignmentScope).HasColumnName("assignment_scope").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RequiresReview).HasColumnName("requires_review");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasIndex(x => x.StructuredExtractionItemId).IsUnique();
        b.HasIndex(x => x.GlassTypeId);
        b.HasOne(x => x.StructuredExtractionItem).WithOne(x => x.GlassDetection)
            .HasForeignKey<StructuredExtractionItemGlassDetection>(x => x.StructuredExtractionItemId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.GlassType).WithMany()
            .HasForeignKey(x => x.GlassTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.ReviewReasons).WithOne(x => x.GlassDetection)
            .HasForeignKey(x => x.GlassDetectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.SourcePages).WithOne(x => x.GlassDetection)
            .HasForeignKey(x => x.GlassDetectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Evidence).WithOne(x => x.GlassDetection)
            .HasForeignKey(x => x.GlassDetectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class StructuredGlassChildConfiguration
{
    public static void Configure<T>(EntityTypeBuilder<T> b, string table)
        where T : class
    {
        b.ToTable(table, "core");
        b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.HasKey("Id");
        b.Property<Guid>("GlassDetectionId").HasColumnName("glass_detection_id").HasColumnType("uuid");
        b.Property<int>("Sequence").HasColumnName("sequence");
        b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasIndex("GlassDetectionId", "Sequence").IsUnique();
    }
}

public sealed class StructuredExtractionItemGlassReviewReasonConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemGlassReviewReason>
{
    public void Configure(EntityTypeBuilder<StructuredExtractionItemGlassReviewReason> b)
    {
        StructuredGlassChildConfiguration.Configure(b, "structured_extraction_item_glass_review_reasons");
        b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasMaxLength(40);
        b.HasIndex(x => new { x.GlassDetectionId, x.Code }).IsUnique();
        b.ToTable("structured_extraction_item_glass_review_reasons", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_item_glass_review_reason_code",
                "\"code\" IN ('GlassTypeNotIdentified', 'GlassTypeAmbiguous', 'GlassTypeConflict')");
            t.HasCheckConstraint("ck_structured_item_glass_review_reason_sequence", "\"sequence\" > 0");
        });
    }
}

public sealed class StructuredExtractionItemGlassSourcePageConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemGlassSourcePage>
{
    public void Configure(EntityTypeBuilder<StructuredExtractionItemGlassSourcePage> b)
    {
        StructuredGlassChildConfiguration.Configure(b, "structured_extraction_item_glass_source_pages");
        b.Property(x => x.PageNumber).HasColumnName("page_number");
        b.HasIndex(x => new { x.GlassDetectionId, x.PageNumber }).IsUnique();
        b.ToTable("structured_extraction_item_glass_source_pages", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_item_glass_source_page_positive", "\"page_number\" > 0");
            t.HasCheckConstraint("ck_structured_item_glass_source_page_sequence", "\"sequence\" > 0");
        });
    }
}

public sealed class StructuredExtractionItemGlassEvidenceConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemGlassEvidence>
{
    public void Configure(EntityTypeBuilder<StructuredExtractionItemGlassEvidence> b)
    {
        StructuredGlassChildConfiguration.Configure(b, "structured_extraction_item_glass_evidence");
        b.Property(x => x.PageNumber).HasColumnName("page_number");
        b.Property(x => x.SourceType).HasColumnName("source_type").HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(500);
        b.HasIndex(x => new { x.GlassDetectionId, x.PageNumber, x.SourceType, x.Text }).IsUnique();
        b.ToTable("structured_extraction_item_glass_evidence", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_item_glass_evidence_page_positive", "\"page_number\" > 0");
            t.HasCheckConstraint("ck_structured_item_glass_evidence_source_type", "\"source_type\" IN ('Native', 'Ocr')");
            t.HasCheckConstraint("ck_structured_item_glass_evidence_sequence", "\"sequence\" > 0");
        });
    }
}
