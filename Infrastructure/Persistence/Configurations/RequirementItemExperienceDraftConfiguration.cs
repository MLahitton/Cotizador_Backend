using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementItemExperienceDraftConfiguration
    : IEntityTypeConfiguration<RequirementItemExperienceDraft>
{
    public void Configure(EntityTypeBuilder<RequirementItemExperienceDraft> builder)
    {
        builder.ToTable("requirement_item_experience_drafts", "core");

        builder.HasKey(value => value.Id);

        builder.Property(value => value.CatalogVersion)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(value => value.SpaceTypeCode)
            .HasMaxLength(64);

        builder.Property(value => value.ResolutionState)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(value => value.UpdatedByUserId)
            .IsRequired();

        builder.HasIndex(value => value.TechnicalProposalItemId)
            .IsUnique();

        builder.HasOne(value => value.TechnicalProposal)
            .WithMany()
            .HasForeignKey(value => value.TechnicalProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.TechnicalProposalItem)
            .WithMany()
            .HasForeignKey(value => value.TechnicalProposalItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(value => value.UpdatedByUser)
            .WithMany()
            .HasForeignKey(value => value.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(value => value.Answers)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RequirementItemExperienceAnswerConfiguration
    : IEntityTypeConfiguration<RequirementItemExperienceAnswer>
{
    public void Configure(EntityTypeBuilder<RequirementItemExperienceAnswer> builder)
    {
        builder.ToTable("requirement_item_experience_answers", "core");

        builder.HasKey(value => value.Id);

        builder.Property(value => value.BenefitCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(value => value.OptionCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(value => new { value.DraftId, value.BenefitCode })
            .IsUnique();

        builder.HasOne(value => value.Draft)
            .WithMany(value => value.Answers)
            .HasForeignKey(value => value.DraftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
