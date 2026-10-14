using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementItemExperienceTechnicalSelectionConfiguration
    : IEntityTypeConfiguration<RequirementItemExperienceTechnicalSelection>
{
    public void Configure(
        EntityTypeBuilder<RequirementItemExperienceTechnicalSelection> builder)
    {
        builder.ToTable(
            "requirement_item_experience_technical_selections",
            "core",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_requirement_item_experience_technical_selections_revision",
                    "\"revision\" > 0");
            });

        builder.HasKey(value => value.Id);

        builder.Property(value => value.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(value => value.TechnicalProposalId)
            .HasColumnName("technical_proposal_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.TechnicalProposalItemId)
            .HasColumnName("technical_proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(value => value.SystemId)
            .HasColumnName("system_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(value => value.GlassTypeId)
            .HasColumnName("glass_type_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(value => value.Revision)
            .HasColumnName("revision")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(value => value.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(value => value.UpdatedByUserId)
            .HasColumnName("updated_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasIndex(value => value.TechnicalProposalItemId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_item_exp_technical_sel_item_id");

        builder.HasIndex(value => value.TechnicalProposalId)
            .HasDatabaseName(
                "ix_requirement_item_exp_technical_sel_proposal_id");

        builder.HasIndex(value => value.SystemId)
            .HasDatabaseName(
                "ix_requirement_item_exp_technical_sel_system_id");

        builder.HasIndex(value => value.GlassTypeId)
            .HasDatabaseName(
                "ix_requirement_item_exp_technical_sel_glass_type_id");

        builder.HasIndex(value => value.UpdatedByUserId)
            .HasDatabaseName(
                "ix_requirement_item_exp_technical_sel_updated_by_user_id");

        builder.HasOne(value => value.TechnicalProposal)
            .WithMany()
            .HasForeignKey(value => value.TechnicalProposalId)
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

        builder.HasOne(value => value.UpdatedByUser)
            .WithMany()
            .HasForeignKey(value => value.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
