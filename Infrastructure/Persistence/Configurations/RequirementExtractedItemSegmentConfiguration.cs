using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExtractedItemSegmentConfiguration
    : IEntityTypeConfiguration<RequirementExtractedItemSegment>
{
    public void Configure(EntityTypeBuilder<RequirementExtractedItemSegment> builder)
    {
        builder.ToTable(
            "requirement_extracted_item_segments",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_extracted_item_segments_positive_values",
                "\"sequence\" > 0 " +
                "AND (\"quantity\" IS NULL OR \"quantity\" > 0) " +
                "AND (\"width_millimeters\" IS NULL OR \"width_millimeters\" > 0) " +
                "AND (\"height_millimeters\" IS NULL OR \"height_millimeters\" > 0) " +
                "AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1))"));

        builder.HasKey(segment => segment.Id);

        builder.Property(segment => segment.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(segment => segment.RequirementExtractedItemId)
            .HasColumnName("requirement_extracted_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(segment => segment.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        MapOptionalString(builder, segment => segment.Role, "role");
        builder.Property(segment => segment.WidthMillimeters)
            .HasColumnName("width_millimeters");
        builder.Property(segment => segment.HeightMillimeters)
            .HasColumnName("height_millimeters");
        builder.Property(segment => segment.Quantity)
            .HasColumnName("quantity");
        MapOptionalString(builder, segment => segment.Operation, "operation");
        MapOptionalString(builder, segment => segment.GeometryType, "geometry_type");
        MapOptionalString(builder, segment => segment.EvidenceText, "evidence_text", 500);
        MapOptionalString(builder, segment => segment.SourceId, "source_id");
        builder.Property(segment => segment.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(segment => segment.PageNumber)
            .HasColumnName("page_number");
        MapOptionalString(builder, segment => segment.SheetName, "sheet_name");
        MapOptionalString(builder, segment => segment.CellRange, "cell_range", 50);
        builder.Property(segment => segment.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)");
        builder.Property(segment => segment.ExtractionStatus)
            .HasColumnName("extraction_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(segment => segment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(segment => segment.Item)
            .WithMany(item => item.Segments)
            .HasForeignKey(segment => segment.RequirementExtractedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(segment => new
            {
                segment.RequirementExtractedItemId,
                segment.Sequence
            })
            .IsUnique()
            .HasDatabaseName("ux_requirement_extracted_item_segments_item_sequence");
    }

    private static void MapOptionalString(
        EntityTypeBuilder<RequirementExtractedItemSegment> builder,
        System.Linq.Expressions.Expression<Func<RequirementExtractedItemSegment, string?>> property,
        string columnName,
        int maxLength = 100)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasMaxLength(maxLength)
            .IsRequired(false);
    }
}
