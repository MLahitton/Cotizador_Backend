using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class StructuredDocumentExtractionConfiguration :
    IEntityTypeConfiguration<StructuredDocumentExtraction>
{
    public void Configure(EntityTypeBuilder<StructuredDocumentExtraction> b)
    {
        b.ToTable("structured_document_extractions", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_extractions_counts", "\"item_count\" >= 0 AND \"document_reference_count\" >= 0 AND \"items_requiring_review\" >= 0 AND \"known_quoteable_unit_count\" >= 0");
            t.HasCheckConstraint("ck_structured_extractions_duration", "\"duration_ms\" >= 0");
            t.HasCheckConstraint("ck_structured_extractions_glass_counts",
                "(\"identified_glass_item_count\" IS NULL AND \"glass_items_requiring_review\" IS NULL) OR (\"identified_glass_item_count\" >= 0 AND \"glass_items_requiring_review\" >= 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.DocumentExtractionResultId).HasColumnName("document_extraction_result_id").HasColumnType("uuid");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasColumnType("varchar(30)");
        b.Property(x => x.ProjectName).HasColumnName("project_name").HasColumnType("text");
        b.Property(x => x.ClientName).HasColumnName("client_name").HasColumnType("text");
        b.Property(x => x.Location).HasColumnName("location").HasColumnType("text");
        b.Property(x => x.ItemCount).HasColumnName("item_count");
        b.Property(x => x.DocumentReferenceCount).HasColumnName("document_reference_count");
        b.Property(x => x.ItemsRequiringReview).HasColumnName("items_requiring_review");
        b.Property(x => x.KnownQuoteableUnitCount).HasColumnName("known_quoteable_unit_count");
        b.Property(x => x.IdentifiedGlassItemCount).HasColumnName("identified_glass_item_count");
        b.Property(x => x.GlassItemsRequiringReview).HasColumnName("glass_items_requiring_review");
        b.Property(x => x.ProcessingMethod).HasColumnName("processing_method").HasColumnType("varchar(100)");
        b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.DocumentExtractionResult).WithOne(x => x.StructuredExtraction)
            .HasForeignKey<StructuredDocumentExtraction>(x => x.DocumentExtractionResultId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.DocumentExtractionResultId).IsUnique();
        b.HasMany(x => x.Items).WithOne(x => x.StructuredDocumentExtraction).HasForeignKey(x => x.StructuredDocumentExtractionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Requirements).WithOne(x => x.StructuredDocumentExtraction).HasForeignKey(x => x.StructuredDocumentExtractionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.DocumentReferences).WithOne(x => x.StructuredDocumentExtraction).HasForeignKey(x => x.StructuredDocumentExtractionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Issues).WithOne(x => x.StructuredDocumentExtraction).HasForeignKey(x => x.StructuredDocumentExtractionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Conflicts).WithOne(x => x.StructuredDocumentExtraction).HasForeignKey(x => x.StructuredDocumentExtractionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StructuredExtractionItemTechnicalClassificationConfiguration
    : IEntityTypeConfiguration<StructuredExtractionItemTechnicalClassification>
{
    public void Configure(EntityTypeBuilder<StructuredExtractionItemTechnicalClassification> b)
    {
        b.ToTable("structured_extraction_item_technical_classifications", "core", t =>
        {
            t.HasCheckConstraint("ck_structured_item_technical_confidence",
                "(\"system_confidence\" IS NULL OR \"system_confidence\" >= 0 AND \"system_confidence\" <= 1) AND (\"frame_confidence\" IS NULL OR \"frame_confidence\" >= 0 AND \"frame_confidence\" <= 1) AND (\"finish_confidence\" IS NULL OR \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1)");
            t.HasCheckConstraint("ck_structured_item_technical_review",
                "(\"requires_review\" = false AND cardinality(\"review_reasons\") = 0) OR (\"requires_review\" = true AND cardinality(\"review_reasons\") > 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.StructuredExtractionItemId).HasColumnName("structured_extraction_item_id").HasColumnType("uuid");
        b.Property(x => x.SystemCode).HasColumnName("system_code").HasMaxLength(30);
        b.Property(x => x.SystemOriginalText).HasColumnName("system_original_text").HasMaxLength(500);
        b.Property(x => x.SystemSource).HasColumnName("system_source").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SystemConfidence).HasColumnName("system_confidence").HasPrecision(5, 4);
        b.Property(x => x.FrameCode).HasColumnName("frame_code").HasMaxLength(30);
        b.Property(x => x.FrameOriginalText).HasColumnName("frame_original_text").HasMaxLength(500);
        b.Property(x => x.FrameSource).HasColumnName("frame_source").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FrameConfidence).HasColumnName("frame_confidence").HasPrecision(5, 4);
        b.Property(x => x.FinishCode).HasColumnName("finish_code").HasMaxLength(30);
        b.Property(x => x.FinishOriginalText).HasColumnName("finish_original_text").HasMaxLength(500);
        b.Property(x => x.FinishSource).HasColumnName("finish_source").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FinishConfidence).HasColumnName("finish_confidence").HasPrecision(5, 4);
        b.Property(x => x.RequiresReview).HasColumnName("requires_review");
        b.Property(x => x.ReviewReasons).HasColumnName("review_reasons").HasColumnType("text[]");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.StructuredExtractionItem).WithOne(x => x.TechnicalClassification)
            .HasForeignKey<StructuredExtractionItemTechnicalClassification>(x => x.StructuredExtractionItemId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.StructuredExtractionItemId).IsUnique();
    }
}

public abstract class StructuredChildConfiguration<T> : IEntityTypeConfiguration<T> where T : class
{
    protected abstract string Table { get; }
    protected abstract void Properties(EntityTypeBuilder<T> b);
    public void Configure(EntityTypeBuilder<T> b)
    {
        b.ToTable(Table, "core");
        b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.HasKey("Id");
        b.Property<Guid>("StructuredDocumentExtractionId").HasColumnName("structured_document_extraction_id").HasColumnType("uuid");
        b.Property<int>("Sequence").HasColumnName("sequence");
        b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasIndex("StructuredDocumentExtractionId", "Sequence").IsUnique();
        Properties(b);
    }
}
public sealed class StructuredExtractionItemConfiguration : StructuredChildConfiguration<StructuredExtractionItem>
{
    protected override string Table => "structured_extraction_items";
    protected override void Properties(EntityTypeBuilder<StructuredExtractionItem> b)
    {
        b.Property(x => x.Reference).HasColumnName("reference").HasColumnType("text");
        b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        b.Property(x => x.ElementType).HasColumnName("element_type").HasConversion<string>().HasColumnType("varchar(30)");
        b.Property(x => x.RawMeasurements).HasColumnName("raw_measurements").HasColumnType("text");
        b.Property(x => x.WidthMillimeters).HasColumnName("width_millimeters");
        b.Property(x => x.HeightMillimeters).HasColumnName("height_millimeters");
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.AreaSquareMeters).HasColumnName("area_square_meters").HasPrecision(18, 6);
        b.Property(x => x.Configuration).HasColumnName("configuration").HasMaxLength(500);
        b.Property(x => x.FunctionalType).HasColumnName("functional_type").HasMaxLength(60);
        b.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(60);
        b.Property(x => x.PanelCount).HasColumnName("panel_count");
        b.Property(x => x.MovablePanelCount).HasColumnName("movable_panel_count");
        b.Property(x => x.FixedPanelCount).HasColumnName("fixed_panel_count");
        b.Property(x => x.Modulation).HasColumnName("modulation").HasMaxLength(60);
        b.Property(x => x.OpeningDirection).HasColumnName("opening_direction").HasMaxLength(60);
        b.Property(x => x.SpecialFeatures).HasColumnName("special_features").HasColumnType("text[]");
        b.Property(x => x.GeometryType).HasColumnName("geometry_type").HasMaxLength(60);
        b.Property(x => x.RequiresReview).HasColumnName("requires_review");
        b.ToTable(Table, "core", t => t.HasCheckConstraint("ck_structured_items_values", "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0) AND (\"area_square_meters\" IS NULL OR \"area_square_meters\" > 0) AND (\"panel_count\" IS NULL OR \"panel_count\" > 0) AND (\"movable_panel_count\" IS NULL OR \"movable_panel_count\" >= 0) AND (\"fixed_panel_count\" IS NULL OR \"fixed_panel_count\" >= 0) AND \"sequence\" > 0"));
    }
}
public sealed class StructuredExtractionRequirementConfiguration : StructuredChildConfiguration<StructuredExtractionRequirement>
{
    protected override string Table => "structured_extraction_requirements";
    protected override void Properties(EntityTypeBuilder<StructuredExtractionRequirement> b) { b.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasColumnType("varchar(50)"); b.Property(x => x.Value).HasColumnName("value").HasColumnType("text"); }
}
public sealed class StructuredExtractionDocumentReferenceConfiguration : StructuredChildConfiguration<StructuredExtractionDocumentReference>
{
    protected override string Table => "structured_extraction_document_references";
    protected override void Properties(EntityTypeBuilder<StructuredExtractionDocumentReference> b)
    {
        b.Property(x => x.Reference).HasColumnName("reference").HasColumnType("text");
        b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        b.Property(x => x.Detail).HasColumnName("detail").HasColumnType("text");
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.ToTable(Table, "core", table => table.HasCheckConstraint(
            "ck_structured_extraction_document_references_quantity_positive",
            "\"quantity\" IS NULL OR \"quantity\" > 0"));
    }
}
public sealed class StructuredExtractionIssueConfiguration : StructuredChildConfiguration<StructuredExtractionIssue>
{
    protected override string Table => "structured_extraction_issues";
    protected override void Properties(EntityTypeBuilder<StructuredExtractionIssue> b) { b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasColumnType("varchar(80)"); b.Property(x => x.Message).HasColumnName("message").HasColumnType("text"); b.Property(x => x.ItemSequence).HasColumnName("item_sequence"); b.Property(x => x.PageNumbers).HasColumnName("page_numbers").HasColumnType("integer[]"); }
}
public sealed class StructuredExtractionConflictConfiguration : StructuredChildConfiguration<StructuredExtractionConflict>
{
    protected override string Table => "structured_extraction_conflicts";
    protected override void Properties(EntityTypeBuilder<StructuredExtractionConflict> b) { b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasColumnType("varchar(80)"); b.Property(x => x.Message).HasColumnName("message").HasColumnType("text"); b.Property(x => x.ItemSequences).HasColumnName("item_sequences").HasColumnType("integer[]"); b.Property(x => x.PageNumbers).HasColumnName("page_numbers").HasColumnType("integer[]"); }
}
