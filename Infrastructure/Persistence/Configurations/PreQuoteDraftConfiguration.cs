using Domain.Identity;
using Domain.PreQuotes;
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
