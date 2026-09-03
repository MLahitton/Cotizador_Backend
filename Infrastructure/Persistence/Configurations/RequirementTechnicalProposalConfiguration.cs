using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementTechnicalProposalConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposal>
{
    public void Configure(EntityTypeBuilder<RequirementTechnicalProposal> builder)
    {
        builder.ToTable(
            "requirement_technical_proposals",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_requirement_technical_proposals_status",
                "\"status\" IN ('Completed', 'RequiresReview')"));

        builder.HasKey(proposal => proposal.Id);

        builder.Property(proposal => proposal.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(proposal => proposal.RequirementId)
            .HasColumnName("requirement_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(proposal => proposal.RequirementExtractionResultId)
            .HasColumnName("requirement_extraction_result_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(proposal => proposal.RequirementProcessingAttemptId)
            .HasColumnName("requirement_processing_attempt_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(proposal => proposal.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(proposal => proposal.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(proposal => proposal.CommercialRevision)
            .HasColumnName("commercial_revision")
            .HasDefaultValue(1L)
            .IsRequired();

        builder.Property(proposal => proposal.CommercialConfirmedAtUtc)
            .HasColumnName("commercial_confirmed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(proposal => proposal.CommercialConfirmedByUserId)
            .HasColumnName("commercial_confirmed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.HasOne(proposal => proposal.Requirement)
            .WithMany()
            .HasForeignKey(proposal => proposal.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(proposal => proposal.ExtractionResult)
            .WithMany()
            .HasForeignKey(proposal => proposal.RequirementExtractionResultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(proposal => proposal.ProcessingAttempt)
            .WithMany()
            .HasForeignKey(proposal => proposal.RequirementProcessingAttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(proposal => proposal.CommercialConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(proposal => proposal.Items)
            .WithOne(item => item.TechnicalProposal)
            .HasForeignKey(item => item.TechnicalProposalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(proposal => proposal.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(proposal => proposal.RequirementId)
            .HasDatabaseName("ix_requirement_technical_proposals_requirement_id");

        builder.HasIndex(proposal => proposal.RequirementExtractionResultId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_technical_proposals_extraction_result_id");

        builder.HasIndex(proposal => proposal.RequirementProcessingAttemptId)
            .IsUnique()
            .HasDatabaseName(
                "ux_requirement_technical_proposals_processing_attempt_id");

        builder.HasIndex(proposal => proposal.CommercialConfirmedByUserId)
            .HasDatabaseName(
                "ix_requirement_technical_proposals_commercial_confirmed_by_user_id");

        builder.HasIndex(proposal => new
            {
                proposal.RequirementId,
                proposal.CreatedAtUtc,
                proposal.Id
            })
            .HasDatabaseName(
                "ix_requirement_technical_proposals_requirement_created_id");
    }
}
