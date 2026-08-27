using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExtractedItemConfiguration
    : IEntityTypeConfiguration<RequirementExtractedItem>
{
    public void Configure(EntityTypeBuilder<RequirementExtractedItem> builder)
    {
        builder.ToTable(
            "requirement_extracted_items",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_extracted_items_positive_values",
                "\"sequence\" > 0 " +
                "AND (\"quantity\" IS NULL OR \"quantity\" > 0) " +
                "AND (\"width_millimeters\" IS NULL OR \"width_millimeters\" > 0) " +
                "AND (\"height_millimeters\" IS NULL OR \"height_millimeters\" > 0) " +
                "AND (\"area_square_meters\" IS NULL OR \"area_square_meters\" > 0) " +
                "AND (\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)) " +
                "AND (\"glass_thickness_mm\" IS NULL OR \"glass_thickness_mm\" > 0)"));

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(item => item.RequirementExtractionResultId)
            .HasColumnName("requirement_extraction_result_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.Ai2ElementId)
            .HasColumnName("ai2_element_id")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(item => item.Sequence)
            .HasColumnName("sequence")
            .IsRequired();

        builder.Property(item => item.Reference)
            .HasColumnName("reference")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(item => item.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(item => item.ElementType)
            .HasColumnName("element_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.WidthMillimeters).HasColumnName("width_millimeters");
        builder.Property(item => item.HeightMillimeters).HasColumnName("height_millimeters");
        builder.Property(item => item.AreaSquareMeters)
            .HasColumnName("area_square_meters")
            .HasColumnType("numeric(12,4)");
        builder.Property(item => item.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)");

        builder.Property(item => item.ExtractionStatus)
            .HasColumnName("extraction_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(item => item.RequiresReview)
            .HasColumnName("requires_review")
            .IsRequired();

        builder.Property(item => item.ReviewReasons)
            .HasColumnName("review_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        MapOptionalString(builder, item => item.FunctionalType, "functional_type");
        MapOptionalString(builder, item => item.Operation, "operation");
        builder.Property(item => item.PanelCount).HasColumnName("panel_count");
        builder.Property(item => item.MovablePanelCount).HasColumnName("movable_panel_count");
        builder.Property(item => item.FixedPanelCount).HasColumnName("fixed_panel_count");
        MapOptionalString(builder, item => item.Arrangement, "arrangement");
        MapOptionalString(builder, item => item.Modulation, "modulation");
        MapOptionalString(builder, item => item.OpeningDirection, "opening_direction");
        builder.Property(item => item.SpecialFeatures)
            .HasColumnName("special_features")
            .HasColumnType("text[]")
            .IsRequired();
        MapOptionalString(builder, item => item.GeometryType, "geometry_type");
        MapOptionalString(builder, item => item.AssemblyType, "assembly_type");
        MapOptionalString(builder, item => item.RequestedSystemRaw, "requested_system_raw", 200);
        MapOptionalString(builder, item => item.RequestedProfileRaw, "requested_profile_raw", 200);
        MapOptionalString(builder, item => item.GlassRawSpecification, "glass_raw_specification", 500);
        MapOptionalString(builder, item => item.GlassTypeRaw, "glass_type_raw");
        MapOptionalString(builder, item => item.GlassTypeNormalized, "glass_type_normalized");
        builder.Property(item => item.GlassThicknessMm)
            .HasColumnName("glass_thickness_mm")
            .HasColumnType("numeric(8,3)");
        MapOptionalString(builder, item => item.GlassColorRaw, "glass_color_raw");
        MapOptionalString(builder, item => item.GlassColorNormalized, "glass_color_normalized");
        MapOptionalString(builder, item => item.GlassTreatmentRaw, "glass_treatment_raw");
        MapOptionalString(builder, item => item.GlassTreatmentNormalized, "glass_treatment_normalized");
        MapOptionalString(builder, item => item.GlassComposition, "glass_composition");
        MapOptionalString(builder, item => item.GlassCoating, "glass_coating");
        MapOptionalString(builder, item => item.GlassTransparency, "glass_transparency");
        builder.Property(item => item.GlassRequiresReview)
            .HasColumnName("glass_requires_review");
        MapOptionalString(builder, item => item.FinishRawDescription, "finish_raw_description", 500);
        MapOptionalString(builder, item => item.FinishNormalizedType, "finish_normalized_type");
        MapOptionalString(builder, item => item.FinishColorRaw, "finish_color_raw");
        MapOptionalString(builder, item => item.FinishColorNormalized, "finish_color_normalized");
        MapOptionalString(builder, item => item.FinishTextureRaw, "finish_texture_raw");
        MapOptionalString(builder, item => item.FinishTextureNormalized, "finish_texture_normalized");
        MapOptionalString(builder, item => item.FinishExplicitCode, "finish_explicit_code");
        builder.Property(item => item.FinishRequiresReview)
            .HasColumnName("finish_requires_review");

        builder.Property(item => item.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(item => item.ExtractionResult)
            .WithMany(result => result.Items)
            .HasForeignKey(item => item.RequirementExtractionResultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.Evidence)
            .WithOne(evidence => evidence.Item)
            .HasForeignKey(evidence => evidence.RequirementExtractedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.Segments)
            .WithOne(segment => segment.Item)
            .HasForeignKey(segment => segment.RequirementExtractedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(item => item.Evidence)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(item => item.Segments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(item => new
            {
                item.RequirementExtractionResultId,
                item.Sequence
            })
            .IsUnique()
            .HasDatabaseName("ux_requirement_extracted_items_extraction_sequence");
    }

    private static void MapOptionalString(
        EntityTypeBuilder<RequirementExtractedItem> builder,
        System.Linq.Expressions.Expression<Func<RequirementExtractedItem, string?>> property,
        string columnName,
        int maxLength = 100)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasMaxLength(maxLength)
            .IsRequired(false);
    }
}
