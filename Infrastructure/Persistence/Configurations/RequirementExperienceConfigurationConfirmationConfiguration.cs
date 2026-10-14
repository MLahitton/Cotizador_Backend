using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementExperienceConfigurationConfirmationConfiguration
    : IEntityTypeConfiguration<RequirementExperienceConfigurationConfirmation>
{
    public void Configure(
        EntityTypeBuilder<RequirementExperienceConfigurationConfirmation> builder)
    {
        builder.ToTable(
            "requirement_experience_configuration_confirmations",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_experience_config_confirmations_revision",
                    "\"revision\" > 0");
            });

        builder.HasKey(value => value.Id);

        builder.Property(value => value.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(value => value.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.TechnicalProposalId)
            .HasColumnName("technical_proposal_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.Revision)
            .HasColumnName("revision")
            .IsRequired();

        builder.Property(value => value.ConfirmedAtUtc)
            .HasColumnName("confirmed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(value => value.ConfirmedByUserId)
            .HasColumnName("confirmed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasIndex(value => value.RequirementId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmations_requirement_id");

        builder.HasIndex(value => value.TechnicalProposalId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmations_proposal_id");

        builder.HasIndex(value => new
            {
                value.TechnicalProposalId,
                value.Revision
            })
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_exp_config_confirmations_proposal_revision");

        builder.HasIndex(value => value.ConfirmedByUserId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmations_confirmed_by_user_id");

        builder.HasOne(value => value.Requirement)
            .WithMany()
            .HasForeignKey(value => value.RequirementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.TechnicalProposal)
            .WithMany()
            .HasForeignKey(value => value.TechnicalProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(value => value.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(value => value.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RequirementExperienceConfigurationConfirmationItemConfiguration
    : IEntityTypeConfiguration<RequirementExperienceConfigurationConfirmationItem>
{
    public void Configure(
        EntityTypeBuilder<RequirementExperienceConfigurationConfirmationItem> builder)
    {
        builder.ToTable(
            "requirement_experience_configuration_confirmation_items",
            "core");

        builder.HasKey(value => value.Id);

        builder.Property(value => value.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(value => value.ConfirmationId)
            .HasColumnName("confirmation_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.TechnicalProposalItemId)
            .HasColumnName("technical_proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.IsIncluded)
            .HasColumnName("is_included")
            .IsRequired();

        builder.Property(value => value.Quantity)
            .HasColumnName("quantity");

        builder.Property(value => value.WidthMillimeters)
            .HasColumnName("width_mm");

        builder.Property(value => value.HeightMillimeters)
            .HasColumnName("height_mm");

        builder.Property(value => value.AreaSquareMeters)
            .HasColumnName("area_m2")
            .HasPrecision(18, 6);

        builder.Property(value => value.Location)
            .HasColumnName("location")
            .HasMaxLength(500);

        builder.Property(value => value.SystemId)
            .HasColumnName("system_id")
            .HasColumnType("uuid");

        builder.Property(value => value.GlassTypeId)
            .HasColumnName("glass_type_id")
            .HasColumnType("uuid");

        builder.Property(value => value.FinishTypeId)
            .HasColumnName("finish_type_id")
            .HasColumnType("uuid");

        builder.Property(value => value.SystemState)
            .HasColumnName("system_state")
            .HasMaxLength(64);

        builder.Property(value => value.GlassState)
            .HasColumnName("glass_state")
            .HasMaxLength(64);

        builder.Property(value => value.FinishState)
            .HasColumnName("finish_state")
            .HasMaxLength(64);

        builder.Property(value => value.SystemOverrideState)
            .HasColumnName("system_override_state")
            .HasMaxLength(64);

        builder.Property(value => value.GlassOverrideState)
            .HasColumnName("glass_override_state")
            .HasMaxLength(64);

        builder.Property(value => value.ReviewReasons)
            .HasColumnName("review_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(value => value.BlockingReasons)
            .HasColumnName("blocking_reasons")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasIndex(value => value.ConfirmationId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmation_items_confirmation_id");

        builder.HasIndex(value => new
            {
                value.ConfirmationId,
                value.TechnicalProposalItemId
            })
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_exp_config_confirmation_items_confirmation_item");

        builder.HasIndex(value => value.TechnicalProposalItemId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmation_items_technical_item_id");

        builder.HasIndex(value => value.SystemId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmation_items_system_id");

        builder.HasIndex(value => value.GlassTypeId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmation_items_glass_type_id");

        builder.HasIndex(value => value.FinishTypeId)
            .HasDatabaseName(
                "ix_requirement_exp_config_confirmation_items_finish_type_id");

        builder.HasOne(value => value.Confirmation)
            .WithMany(value => value.Items)
            .HasForeignKey(value => value.ConfirmationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.TechnicalProposalItem)
            .WithMany()
            .HasForeignKey(value => value.TechnicalProposalItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Domain.Catalogs.ProductSystem>()
            .WithMany()
            .HasForeignKey(value => value.SystemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.GlassType>()
            .WithMany()
            .HasForeignKey(value => value.GlassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Catalogs.FinishType>()
            .WithMany()
            .HasForeignKey(value => value.FinishTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
