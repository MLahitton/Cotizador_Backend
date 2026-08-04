using Domain.Identity;
using Domain.PreQuotes;
using Domain.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PreQuoteDraftConfiguration
    : IEntityTypeConfiguration<PreQuoteDraft>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraft> b)
    {
        b.ToTable("pre_quote_drafts", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_drafts_version", "\"version\" > 0");
            t.HasCheckConstraint("ck_pre_quote_drafts_approval",
                "(\"status\" = 'Approved' AND \"approved_by_user_id\" IS NOT NULL AND \"approved_at_utc\" IS NOT NULL) OR (\"status\" <> 'Approved' AND \"approved_by_user_id\" IS NULL AND \"approved_at_utc\" IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PreQuoteId).HasColumnName("pre_quote_id");
        b.Property(x => x.SourceDocumentId).HasColumnName("source_document_id");
        b.Property(x => x.SourceStructuredExtractionId).HasColumnName("source_structured_extraction_id");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ProjectName).HasColumnName("project_name").HasMaxLength(500);
        b.Property(x => x.ClientName).HasColumnName("client_name").HasMaxLength(500);
        b.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        b.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc").HasColumnType("timestamp with time zone");
        b.HasOne(x => x.PreQuote).WithOne().HasForeignKey<PreQuoteDraft>(x => x.PreQuoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceStructuredExtraction).WithMany().HasForeignKey(x => x.SourceStructuredExtractionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PreQuoteId).IsUnique();
        b.HasIndex(x => x.SourceDocumentId);
        b.HasIndex(x => x.SourceStructuredExtractionId);
        b.HasMany(x => x.Items).WithOne(x => x.Draft).HasForeignKey(x => x.PreQuoteDraftId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Requirements).WithOne(x => x.Draft).HasForeignKey(x => x.PreQuoteDraftId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.DocumentReferences).WithOne(x => x.Draft).HasForeignKey(x => x.PreQuoteDraftId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Issues).WithOne(x => x.Draft).HasForeignKey(x => x.PreQuoteDraftId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Conflicts).WithOne(x => x.Draft).HasForeignKey(x => x.PreQuoteDraftId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class DraftChild
{
    public static void Base<T>(
        EntityTypeBuilder<T> b,
        string table,
        bool audit = true) where T : class
    {
        b.ToTable(table, "core", t => t.HasCheckConstraint(
            $"ck_{table}_sequence", "\"sequence\" > 0"));
        b.Property<Guid>("Id").HasColumnName("id").ValueGeneratedNever();
        b.HasKey("Id");
        b.Property<Guid>("PreQuoteDraftId").HasColumnName("pre_quote_draft_id");
        b.Property<int>("Sequence").HasColumnName("sequence");
        b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        b.HasIndex("PreQuoteDraftId", "Sequence").IsUnique();
        if (audit)
        {
            b.Property<Guid>("CreatedByUserId").HasColumnName("created_by_user_id");
            b.Property<Guid>("UpdatedByUserId").HasColumnName("updated_by_user_id");
            b.Property<DateTimeOffset>("UpdatedAtUtc").HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        }
    }
}

public sealed class PreQuoteDraftItemConfiguration : IEntityTypeConfiguration<PreQuoteDraftItem>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItem> b)
    {
        DraftChild.Base(b, "pre_quote_draft_items");
        b.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SourceStructuredItemId).HasColumnName("source_structured_item_id");
        b.Property(x => x.SourceItemSequence).HasColumnName("source_item_sequence");
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200);
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(x => x.ElementType).HasColumnName("element_type").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RawMeasurements).HasColumnName("raw_measurements").HasMaxLength(500);
        b.Property(x => x.WidthMillimeters).HasColumnName("width_millimeters");
        b.Property(x => x.HeightMillimeters).HasColumnName("height_millimeters");
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.IsIncluded).HasColumnName("is_included");
        b.ToTable("pre_quote_draft_items", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_items_values",
                "(\"width_millimeters\" IS NULL AND \"height_millimeters\" IS NULL OR \"width_millimeters\" > 0 AND \"height_millimeters\" > 0) AND (\"quantity\" IS NULL OR \"quantity\" > 0)");
            t.HasCheckConstraint("ck_pre_quote_draft_items_origin",
                "(\"origin\" = 'Ai' AND \"source_structured_item_id\" IS NOT NULL AND \"source_item_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_item_id\" IS NULL AND \"source_item_sequence\" IS NULL)");
        });
        b.HasOne<StructuredExtractionItem>().WithMany().HasForeignKey(x => x.SourceStructuredItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GlassSnapshot).WithOne()
            .HasForeignKey<PreQuoteDraftItemGlassSnapshot>(x => x.PreQuoteDraftItemId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ValuationSnapshot).WithOne()
            .HasForeignKey<PreQuoteDraftItemValuationSnapshot>(x => x.PreQuoteDraftItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PreQuoteDraftItemGlassSnapshotConfiguration
    : IEntityTypeConfiguration<PreQuoteDraftItemGlassSnapshot>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItemGlassSnapshot> b)
    {
        b.ToTable("pre_quote_draft_item_glass_snapshots", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_snapshot_scope",
                "\"assignment_scope\" IN ('Item', 'Section', 'General', 'Unassigned')");
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_snapshot_identity",
                "(\"normalized_code_snapshot\" IS NULL AND \"glass_type_id\" IS NULL) OR (\"normalized_code_snapshot\" IS NOT NULL AND \"glass_type_id\" IS NOT NULL)");
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_snapshot_requirements",
                "\"requires_review\" IS NOT NULL");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.PreQuoteDraftItemId).HasColumnName("pre_quote_draft_item_id").HasColumnType("uuid");
        b.Property(x => x.SourceStructuredItemGlassId).HasColumnName("source_structured_item_glass_id").HasColumnType("uuid");
        b.Property(x => x.GlassTypeId).HasColumnName("glass_type_id").HasColumnType("uuid");
        b.Property(x => x.RawSpecification).HasColumnName("raw_specification").HasMaxLength(500);
        b.Property(x => x.NormalizedCodeSnapshot).HasColumnName("normalized_code_snapshot").HasMaxLength(30);
        b.Property(x => x.AssignmentScope).HasColumnName("assignment_scope").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RequiresReview).HasColumnName("requires_review");
        b.HasIndex(x => x.PreQuoteDraftItemId).IsUnique();
    }
}

public sealed class PreQuoteDraftItemValuationSnapshotConfiguration
    : IEntityTypeConfiguration<PreQuoteDraftItemValuationSnapshot>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItemValuationSnapshot> b)
    {
        b.ToTable("pre_quote_draft_item_valuation_snapshots", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_areas",
                "\"unit_area_square_meters\" IS NULL OR \"unit_area_square_meters\" >= 0 AND \"total_area_square_meters\" >= 0");
            t.HasCheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_amounts",
                "\"unit_amount\" IS NULL OR \"unit_amount\" >= 0 AND \"total_amount\" >= \"unit_amount\"");
            t.HasCheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_prices",
                "\"unit_price_per_square_meter\" IS NULL OR \"unit_price_per_square_meter\" > 0");
            t.HasCheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_currency",
                "\"currency\" IS NULL OR char_length(\"currency\") = 3");
            t.HasCheckConstraint("ck_pre_quote_draft_item_valuation_snapshot_status",
                "\"status\" IN ('NotApplicable', 'Pending', 'Valued', 'Stale', 'RequiresReview')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.PreQuoteDraftItemId).HasColumnName("pre_quote_draft_item_id").HasColumnType("uuid");
        b.Property(x => x.SourceStructuredItemValuationId).HasColumnName("source_structured_item_valuation_id").HasColumnType("uuid");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.GlassTypeId).HasColumnName("glass_type_id").HasColumnType("uuid");
        b.Property(x => x.GlassPriceRangeVersionId).HasColumnName("glass_price_range_version_id").HasColumnType("uuid");
        b.Property(x => x.WidthMillimetersUsed).HasColumnName("width_millimeters_used");
        b.Property(x => x.HeightMillimetersUsed).HasColumnName("height_millimeters_used");
        b.Property(x => x.QuantityUsed).HasColumnName("quantity_used");
        b.Property(x => x.UnitAreaSquareMeters).HasColumnName("unit_area_square_meters").HasPrecision(18, 6);
        b.Property(x => x.TotalAreaSquareMeters).HasColumnName("total_area_square_meters").HasPrecision(18, 6);
        b.Property(x => x.UnitPricePerSquareMeter).HasColumnName("unit_price_per_square_meter").HasPrecision(18, 6);
        b.Property(x => x.UnitAmount).HasColumnName("unit_amount").HasPrecision(18, 6);
        b.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 6);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.ValuedAtUtc).HasColumnName("valued_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.InvalidatedAtUtc).HasColumnName("invalidated_at_utc").HasColumnType("timestamp with time zone");
        b.Property(x => x.InvalidationReason).HasColumnName("invalidation_reason").HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => x.PreQuoteDraftItemId).IsUnique();
        b.HasOne<GlassType>().WithMany().HasForeignKey(x => x.GlassTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GlassPriceRangeVersion>().WithMany().HasForeignKey(x => x.GlassPriceRangeVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PreQuoteDraftItemGlassReviewReasonConfiguration
    : IEntityTypeConfiguration<PreQuoteDraftItemGlassReviewReason>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItemGlassReviewReason> b)
    {
        b.ToTable("pre_quote_draft_item_glass_review_reasons", "core");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.GlassSnapshotId).HasColumnName("glass_snapshot_id").HasColumnType("uuid");
        b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasMaxLength(40);
        b.HasOne<PreQuoteDraftItemGlassSnapshot>()
            .WithMany(x => x.ReviewReasons)
            .HasForeignKey(x => x.GlassSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.GlassSnapshotId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.GlassSnapshotId, x.Code }).IsUnique();
        b.ToTable("pre_quote_draft_item_glass_review_reasons", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_review_reason_sequence", "\"sequence\" > 0");
        });
    }
}

public sealed class PreQuoteDraftItemGlassSourcePageConfiguration
    : IEntityTypeConfiguration<PreQuoteDraftItemGlassSourcePage>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItemGlassSourcePage> b)
    {
        b.ToTable("pre_quote_draft_item_glass_source_pages", "core");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.GlassSnapshotId).HasColumnName("glass_snapshot_id").HasColumnType("uuid");
        b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.PageNumber).HasColumnName("page_number");
        b.HasOne<PreQuoteDraftItemGlassSnapshot>()
            .WithMany(x => x.SourcePages)
            .HasForeignKey(x => x.GlassSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.GlassSnapshotId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.GlassSnapshotId, x.PageNumber }).IsUnique();
        b.ToTable("pre_quote_draft_item_glass_source_pages", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_source_page_sequence", "\"sequence\" > 0");
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_source_page_page_number", "\"page_number\" > 0");
        });
    }
}

public sealed class PreQuoteDraftItemGlassEvidenceConfiguration
    : IEntityTypeConfiguration<PreQuoteDraftItemGlassEvidence>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftItemGlassEvidence> b)
    {
        b.ToTable("pre_quote_draft_item_glass_evidence", "core");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        b.Property(x => x.GlassSnapshotId).HasColumnName("glass_snapshot_id").HasColumnType("uuid");
        b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.PageNumber).HasColumnName("page_number");
        b.Property(x => x.SourceType).HasColumnName("source_type").HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(500);
        b.HasOne<PreQuoteDraftItemGlassSnapshot>()
            .WithMany(x => x.Evidence)
            .HasForeignKey(x => x.GlassSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.GlassSnapshotId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.GlassSnapshotId, x.PageNumber, x.SourceType, x.Text }).IsUnique();
        b.ToTable("pre_quote_draft_item_glass_evidence", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_evidence_sequence", "\"sequence\" > 0");
            t.HasCheckConstraint("ck_pre_quote_draft_item_glass_evidence_page_number", "\"page_number\" > 0");
        });
    }
}

public sealed class PreQuoteDraftRequirementConfiguration : IEntityTypeConfiguration<PreQuoteDraftRequirement>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftRequirement> b)
    {
        DraftChild.Base(b, "pre_quote_draft_requirements");
        b.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SourceStructuredRequirementId).HasColumnName("source_structured_requirement_id");
        b.Property(x => x.SourceRequirementSequence).HasColumnName("source_requirement_sequence");
        b.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(50);
        b.Property(x => x.Value).HasColumnName("value").HasMaxLength(1000);
        b.Property(x => x.IsIncluded).HasColumnName("is_included");
        b.ToTable("pre_quote_draft_requirements", "core", t => t.HasCheckConstraint(
            "ck_pre_quote_draft_requirements_origin",
            "(\"origin\" = 'Ai' AND \"source_structured_requirement_id\" IS NOT NULL AND \"source_requirement_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_requirement_id\" IS NULL AND \"source_requirement_sequence\" IS NULL)"));
        b.HasOne<StructuredExtractionRequirement>().WithMany().HasForeignKey(x => x.SourceStructuredRequirementId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PreQuoteDraftDocumentReferenceConfiguration : IEntityTypeConfiguration<PreQuoteDraftDocumentReference>
{
    public void Configure(EntityTypeBuilder<PreQuoteDraftDocumentReference> b)
    {
        DraftChild.Base(b, "pre_quote_draft_document_references");
        b.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SourceStructuredDocumentReferenceId).HasColumnName("source_structured_document_reference_id");
        b.Property(x => x.SourceDocumentReferenceSequence).HasColumnName("source_document_reference_sequence");
        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200);
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(2000);
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.IsIncluded).HasColumnName("is_included");
        b.ToTable("pre_quote_draft_document_references", "core", t =>
        {
            t.HasCheckConstraint("ck_pre_quote_draft_document_references_quantity", "\"quantity\" IS NULL OR \"quantity\" > 0");
            t.HasCheckConstraint("ck_pre_quote_draft_document_references_origin",
                "(\"origin\" = 'Ai' AND \"source_structured_document_reference_id\" IS NOT NULL AND \"source_document_reference_sequence\" IS NOT NULL) OR (\"origin\" = 'Manual' AND \"source_structured_document_reference_id\" IS NULL AND \"source_document_reference_sequence\" IS NULL)");
        });
        b.HasOne<StructuredExtractionDocumentReference>().WithMany().HasForeignKey(x => x.SourceStructuredDocumentReferenceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public abstract class DraftFindingConfiguration<T> : IEntityTypeConfiguration<T>
    where T : PreQuoteDraftFinding
{
    protected abstract string Table { get; }
    public virtual void Configure(EntityTypeBuilder<T> b)
    {
        DraftChild.Base(b, Table, false);
        b.Property(x => x.ResolutionStatus).HasColumnName("resolution_status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(2000);
        b.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        b.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasColumnType("timestamp with time zone");
        b.ToTable(Table, "core", t => t.HasCheckConstraint($"ck_{Table}_resolution",
            "(\"resolution_status\" = 'Pending' AND \"resolution_note\" IS NULL AND \"resolved_by_user_id\" IS NULL AND \"resolved_at_utc\" IS NULL) OR (\"resolution_status\" IN ('Resolved','Dismissed') AND \"resolution_note\" IS NOT NULL AND \"resolved_by_user_id\" IS NOT NULL AND \"resolved_at_utc\" IS NOT NULL)"));
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class PreQuoteDraftIssueConfiguration : DraftFindingConfiguration<PreQuoteDraftIssue>
{
    protected override string Table => "pre_quote_draft_issues";
    public override void Configure(EntityTypeBuilder<PreQuoteDraftIssue> b)
    {
        base.Configure(b);
        b.Property(x => x.SourceStructuredIssueId).HasColumnName("source_structured_issue_id");
        b.Property(x => x.SourceIssueSequence).HasColumnName("source_issue_sequence");
        b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasMaxLength(80);
        b.Property(x => x.Message).HasColumnName("message");
        b.Property(x => x.ItemSequence).HasColumnName("item_sequence");
        b.Property(x => x.PageNumbers).HasColumnName("page_numbers").HasColumnType("integer[]");
        b.HasOne<StructuredExtractionIssue>().WithMany().HasForeignKey(x => x.SourceStructuredIssueId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class PreQuoteDraftConflictConfiguration : DraftFindingConfiguration<PreQuoteDraftConflict>
{
    protected override string Table => "pre_quote_draft_conflicts";
    public override void Configure(EntityTypeBuilder<PreQuoteDraftConflict> b)
    {
        base.Configure(b);
        b.Property(x => x.SourceStructuredConflictId).HasColumnName("source_structured_conflict_id");
        b.Property(x => x.SourceConflictSequence).HasColumnName("source_conflict_sequence");
        b.Property(x => x.Code).HasColumnName("code").HasConversion<string>().HasMaxLength(80);
        b.Property(x => x.Message).HasColumnName("message");
        b.Property(x => x.ItemSequences).HasColumnName("item_sequences").HasColumnType("integer[]");
        b.Property(x => x.PageNumbers).HasColumnName("page_numbers").HasColumnType("integer[]");
        b.HasOne<StructuredExtractionConflict>().WithMany().HasForeignKey(x => x.SourceStructuredConflictId).OnDelete(DeleteBehavior.Restrict);
    }
}
