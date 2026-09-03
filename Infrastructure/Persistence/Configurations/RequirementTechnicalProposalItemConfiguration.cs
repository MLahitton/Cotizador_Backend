using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementTechnicalProposalItemConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposalItem>
{
    public void Configure(EntityTypeBuilder<RequirementTechnicalProposalItem> builder)
    {
        builder.ToTable(
            "requirement_technical_proposal_items",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_technical_proposal_items_confidence",
                    "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 " +
                    "AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 " +
                    "AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 " +
                    "AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 " +
                    "AND (\"historical_best_similarity\" IS NULL OR " +
                    "(\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) " +
                    "AND (\"historical_average_similarity\" IS NULL OR " +
                    "(\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1)) " +
                    "AND (\"manual_quantity_override\" IS NULL OR \"manual_quantity_override\" > 0) " +
                    "AND (\"manual_width_millimeters_override\" IS NULL OR \"manual_width_millimeters_override\" > 0) " +
                    "AND (\"manual_height_millimeters_override\" IS NULL OR \"manual_height_millimeters_override\" > 0)");

                tableBuilder.HasCheckConstraint(
                    "ck_requirement_technical_proposal_items_historical_support",
                    "\"historical_support_count\" >= 0");
            });

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(item => item.TechnicalProposalId)
            .HasColumnName("technical_proposal_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.RequirementExtractedItemId)
            .HasColumnName("requirement_extracted_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.SuggestedSystemId)
            .HasColumnName("suggested_system_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SuggestedGlassTypeId)
            .HasColumnName("suggested_glass_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SuggestedFinishTypeId)
            .HasColumnName("suggested_finish_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SelectedSystemId)
            .HasColumnName("selected_system_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SelectedGlassTypeId)
            .HasColumnName("selected_glass_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SelectedFinishTypeId)
            .HasColumnName("selected_finish_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.SelectedAtUtc)
            .HasColumnName("selected_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(item => item.SelectedByUserId)
            .HasColumnName("selected_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.ManualQuantityOverride)
            .HasColumnName("manual_quantity_override")
            .IsRequired(false);

        builder.Property(item => item.ManualWidthMillimetersOverride)
            .HasColumnName("manual_width_millimeters_override")
            .IsRequired(false);

        builder.Property(item => item.ManualHeightMillimetersOverride)
            .HasColumnName("manual_height_millimeters_override")
            .IsRequired(false);

        builder.Property(item => item.InclusionState)
            .HasColumnName("inclusion_state")
            .HasColumnType("varchar(20)")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(item => item.ExcludedAtUtc)
            .HasColumnName("excluded_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(item => item.ExcludedByUserId)
            .HasColumnName("excluded_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(item => item.ExclusionReason)
            .HasColumnName("exclusion_reason")
            .HasColumnType("varchar(500)")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(item => item.OverallConfidence)
            .HasColumnName("overall_confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(item => item.SystemConfidence)
            .HasColumnName("system_confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(item => item.GlassConfidence)
            .HasColumnName("glass_confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(item => item.FinishConfidence)
            .HasColumnName("finish_confidence")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(item => item.RequiresReview)
            .HasColumnName("requires_review")
            .IsRequired();

        builder.Property(item => item.IsTechnicallyComplete)
            .HasColumnName("is_technically_complete")
            .IsRequired();

        builder.Property(item => item.IsPriceable)
            .HasColumnName("is_priceable")
            .IsRequired();

        builder.Property(item => item.ReviewReasons)
            .HasColumnName("review_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(item => item.SystemResolutionReasons)
            .HasColumnName("system_resolution_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(item => item.GlassResolutionReasons)
            .HasColumnName("glass_resolution_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(item => item.FinishResolutionReasons)
            .HasColumnName("finish_resolution_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(item => item.HistoricalSupportCount)
            .HasColumnName("historical_support_count")
            .IsRequired();

        builder.Property(item => item.HistoricalBestSimilarity)
            .HasColumnName("historical_best_similarity")
            .HasColumnType("numeric(5,4)")
            .IsRequired(false);

        builder.Property(item => item.HistoricalAverageSimilarity)
            .HasColumnName("historical_average_similarity")
            .HasColumnType("numeric(5,4)")
            .IsRequired(false);

        builder.Property(item => item.HistoricalSimilarityStatus)
            .HasColumnName("historical_similarity_status")
            .HasColumnType("varchar(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(item => item.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(item => item.ExtractedItem)
            .WithMany()
            .HasForeignKey(item => item.RequirementExtractedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(item => item.SuggestedSystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(item => item.SuggestedGlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(item => item.SuggestedFinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(item => item.SelectedSystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(item => item.SelectedGlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(item => item.SelectedFinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(item => item.SelectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(item => item.ExcludedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.SystemAlternatives)
            .WithOne(alternative => alternative.ProposalItem)
            .HasForeignKey(alternative => alternative.ProposalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.GlassAlternatives)
            .WithOne(alternative => alternative.ProposalItem)
            .HasForeignKey(alternative => alternative.ProposalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.FinishAlternatives)
            .WithOne(alternative => alternative.ProposalItem)
            .HasForeignKey(alternative => alternative.ProposalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.HistoricalExamples)
            .WithOne(example => example.ProposalItem)
            .HasForeignKey(example => example.ProposalItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(item => item.SystemAlternatives)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.GlassAlternatives)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.FinishAlternatives)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(item => item.HistoricalExamples)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(item => item.TechnicalProposalId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_proposal_id");

        builder.HasIndex(item => item.RequirementExtractedItemId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_technical_proposal_items_extracted_item_id");

        builder.HasIndex(item => item.SelectedSystemId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_selected_system_id");

        builder.HasIndex(item => item.SelectedGlassTypeId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_selected_glass_type_id");

        builder.HasIndex(item => item.SelectedFinishTypeId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_selected_finish_type_id");

        builder.HasIndex(item => item.SelectedByUserId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_selected_by_user_id");

        builder.HasIndex(item => item.ExcludedByUserId)
            .HasDatabaseName(
                "ix_requirement_technical_proposal_items_excluded_by_user_id");
    }
}
