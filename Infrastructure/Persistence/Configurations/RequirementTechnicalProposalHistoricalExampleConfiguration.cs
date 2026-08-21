using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class RequirementTechnicalProposalHistoricalExampleConfiguration
    : IEntityTypeConfiguration<RequirementTechnicalProposalHistoricalExample>
{
    public void Configure(
        EntityTypeBuilder<RequirementTechnicalProposalHistoricalExample> builder)
    {
        builder.ToTable(
            "requirement_technical_proposal_historical_examples",
            "core",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_req_tech_proposal_hist_examples_similarity",
                "\"similarity_score\" >= 0 AND \"similarity_score\" <= 1"));

        builder.HasKey(example => example.Id);

        builder.Property(example => example.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(example => example.ProposalItemId)
            .HasColumnName("proposal_item_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(example => example.CandidateId)
            .HasColumnName("candidate_id")
            .HasColumnType("varchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(example => example.QuoteId)
            .HasColumnName("quote_id")
            .HasColumnType("varchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(example => example.HistoricalReference)
            .HasColumnName("historical_reference")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(example => example.SimilarityScore)
            .HasColumnName("similarity_score")
            .HasColumnType("numeric(5,4)")
            .IsRequired();

        builder.Property(example => example.MatchedFeatures)
            .HasColumnName("matched_features")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(example => example.Differences)
            .HasColumnName("differences")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(example => example.TechnicalExplanation)
            .HasColumnName("technical_explanation")
            .HasColumnType("varchar(1000)")
            .HasMaxLength(1000)
            .IsRequired();

        builder.HasIndex(example => example.ProposalItemId)
            .HasDatabaseName(
                "ix_req_tech_proposal_hist_examples_item_id");
    }
}
