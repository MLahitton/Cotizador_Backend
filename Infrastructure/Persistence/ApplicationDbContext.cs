using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ExternalIdentity> ExternalIdentities =>
        Set<ExternalIdentity>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<GlassType> GlassTypes => Set<GlassType>();
    public DbSet<GlassPriceRangeVersion> GlassPriceRangeVersions =>
        Set<GlassPriceRangeVersion>();
    public DbSet<ProductSystem> ProductSystems => Set<ProductSystem>();
    public DbSet<ProductSystemConstraint> ProductSystemConstraints =>
        Set<ProductSystemConstraint>();
    public DbSet<FrameType> FrameTypes => Set<FrameType>();
    public DbSet<FinishType> FinishTypes => Set<FinishType>();
    public DbSet<CatalogAlias> CatalogAliases => Set<CatalogAlias>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<PreQuote> PreQuotes => Set<PreQuote>();

    public DbSet<PreQuoteSerialCounter> PreQuoteSerialCounters =>
        Set<PreQuoteSerialCounter>();

    public DbSet<PreQuoteDocument> PreQuoteDocuments =>
        Set<PreQuoteDocument>();

    public DbSet<DocumentProcessingAttempt> DocumentProcessingAttempts =>
        Set<DocumentProcessingAttempt>();

    public DbSet<DocumentExtractionResult> DocumentExtractionResults =>
        Set<DocumentExtractionResult>();

    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RequirementFile> RequirementFiles => Set<RequirementFile>();
    public DbSet<RequirementProcessingAttempt>
        RequirementProcessingAttempts => Set<RequirementProcessingAttempt>();
    public DbSet<RequirementExtractionResult>
        RequirementExtractionResults => Set<RequirementExtractionResult>();
    public DbSet<RequirementExtractedItem>
        RequirementExtractedItems => Set<RequirementExtractedItem>();
    public DbSet<RequirementExtractedItemEvidence>
        RequirementExtractedItemEvidence => Set<RequirementExtractedItemEvidence>();
    public DbSet<RequirementExtractedItemSegment>
        RequirementExtractedItemSegments => Set<RequirementExtractedItemSegment>();
    public DbSet<RequirementTechnicalProposal>
        RequirementTechnicalProposals => Set<RequirementTechnicalProposal>();
    public DbSet<RequirementTechnicalProposalItem>
        RequirementTechnicalProposalItems => Set<RequirementTechnicalProposalItem>();
    public DbSet<RequirementTechnicalProposalSystemAlternative>
        RequirementTechnicalProposalSystemAlternatives =>
            Set<RequirementTechnicalProposalSystemAlternative>();
    public DbSet<RequirementTechnicalProposalGlassAlternative>
        RequirementTechnicalProposalGlassAlternatives =>
            Set<RequirementTechnicalProposalGlassAlternative>();
    public DbSet<RequirementTechnicalProposalFinishAlternative>
        RequirementTechnicalProposalFinishAlternatives =>
            Set<RequirementTechnicalProposalFinishAlternative>();
    public DbSet<RequirementTechnicalProposalHistoricalExample>
        RequirementTechnicalProposalHistoricalExamples =>
            Set<RequirementTechnicalProposalHistoricalExample>();
    public DbSet<RequirementPricingSnapshot> RequirementPricingSnapshots =>
        Set<RequirementPricingSnapshot>();
    public DbSet<RequirementPricingItemSnapshot> RequirementPricingItemSnapshots =>
        Set<RequirementPricingItemSnapshot>();
    public DbSet<RequirementChatThread> RequirementChatThreads =>
        Set<RequirementChatThread>();
    public DbSet<RequirementChatMessage> RequirementChatMessages =>
        Set<RequirementChatMessage>();

    public DbSet<StructuredDocumentExtraction> StructuredDocumentExtractions =>
        Set<StructuredDocumentExtraction>();
    public DbSet<StructuredExtractionItemGlassDetection>
        StructuredExtractionItemGlassDetections =>
            Set<StructuredExtractionItemGlassDetection>();
    public DbSet<StructuredExtractionItemGlassValuation>
        StructuredExtractionItemGlassValuations =>
            Set<StructuredExtractionItemGlassValuation>();
    public DbSet<StructuredExtractionItemTechnicalClassification>
        StructuredExtractionItemTechnicalClassifications =>
            Set<StructuredExtractionItemTechnicalClassification>();

    public DbSet<PreQuoteDraft> PreQuoteDrafts => Set<PreQuoteDraft>();
    public DbSet<PreQuoteDraftItem> PreQuoteDraftItems =>
        Set<PreQuoteDraftItem>();
    public DbSet<PreQuoteDraftItemTechnicalSnapshot>
        PreQuoteDraftItemTechnicalSnapshots =>
            Set<PreQuoteDraftItemTechnicalSnapshot>();
    public DbSet<PreQuoteDraftItemTechnicalSelection>
        PreQuoteDraftItemTechnicalSelections =>
            Set<PreQuoteDraftItemTechnicalSelection>();
    public DbSet<PreQuoteDraftRequirement> PreQuoteDraftRequirements =>
        Set<PreQuoteDraftRequirement>();
    public DbSet<PreQuoteDraftDocumentReference> PreQuoteDraftDocumentReferences =>
        Set<PreQuoteDraftDocumentReference>();
    public DbSet<PreQuoteDraftIssue> PreQuoteDraftIssues =>
        Set<PreQuoteDraftIssue>();
    public DbSet<PreQuoteDraftConflict> PreQuoteDraftConflicts =>
        Set<PreQuoteDraftConflict>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);
    }
}
