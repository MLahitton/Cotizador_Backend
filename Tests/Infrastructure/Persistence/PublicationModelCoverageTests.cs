using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class PublicationModelCoverageTests
{
    [Fact]
    public void EveryPersistedInitialProperty_HasAnExplicitClassification()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used").Options);
        Check<RequirementExtractionResult, PublicationExtraction>("Id RequirementProcessingAttemptId CreatedAtUtc", "Provider", "PayloadJson");
        Check<RequirementExtractedItem, PublicationExtractedItem>("Id RequirementExtractionResultId CreatedAtUtc", "");
        Check<RequirementExtractedItemEvidence, PublicationEvidence>("Id RequirementExtractedItemId CreatedAtUtc", "");
        Check<RequirementExtractedItemSegment, PublicationSegment>("Id RequirementExtractedItemId CreatedAtUtc", "");
        Check<RequirementTechnicalProposal, PublicationProposal>("Id RequirementId RequirementExtractionResultId RequirementProcessingAttemptId CreatedAtUtc",
            "CommercialRevision CommercialConfirmedAtUtc CommercialConfirmedByUserId");
        Check<RequirementTechnicalProposalItem, PublicationProposalItem>("Id TechnicalProposalId RequirementExtractedItemId CreatedAtUtc Source InclusionState",
            "ManualNote ManualLocationOverride ManualObservation SelectedSystemId SelectedGlassTypeId SelectedFinishTypeId SelectedAtUtc SelectedByUserId ManualQuantityOverride ManualWidthMillimetersOverride ManualHeightMillimetersOverride ExcludedAtUtc ExcludedByUserId ExclusionReason");
        Check<RequirementTechnicalProposalSystemAlternative, PublicationAlternative>("Id ProposalItemId", "", "ProductSystemId");
        Check<RequirementTechnicalProposalGlassAlternative, PublicationAlternative>("Id ProposalItemId", "", "GlassTypeId");
        Check<RequirementTechnicalProposalFinishAlternative, PublicationAlternative>("Id ProposalItemId", "", "FinishTypeId");
        Check<RequirementTechnicalProposalHistoricalExample, PublicationHistoricalExample>("Id ProposalItemId", "");
        // Incorporation evidence is not covered by the historical v1 manifest.
        Check<RequirementFile, PublicationFile>("RequirementId", "StoredEvidenceAtUtc StoredEvidenceVersion StoredLengthBytes StoredSha256", "Id");

        void Check<TEntity, TInput>(string generated, string other, string aliases = "")
        {
            var input = typeof(TInput).GetProperties().Select(p => p.Name).ToHashSet();
            var classified = (generated + " " + other + " " + aliases).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var persisted = db.Model.FindEntityType(typeof(TEntity))!.GetProperties().Select(p => p.Name).ToArray();
            Assert.All(persisted, p => Assert.True(input.Contains(p) || classified.Contains(p), $"Unclassified {typeof(TEntity).Name}.{p}"));
            Assert.All(classified, p => Assert.Contains(p, persisted));
        }
    }
}
