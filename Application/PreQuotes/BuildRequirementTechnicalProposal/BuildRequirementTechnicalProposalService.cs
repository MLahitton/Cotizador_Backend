using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Domain.PreQuotes;

namespace Application.PreQuotes.BuildRequirementTechnicalProposal;

public sealed class BuildRequirementTechnicalProposalService(
    IRequirementRepository requirementRepository,
    IProductSystemCatalogRepository productSystemCatalog,
    IGlassTypeCatalogRepository glassCatalog,
    IFinishTypeCatalogRepository finishCatalog,
    IGlassCandidateResolver glassResolver,
    IFinishCandidateResolver finishResolver,
    ResolveHistoricalTechnicalEvidenceService historicalTechnicalEvidence)
{
    private const string SystemAlternativeReason = "SYSTEM_ALTERNATIVE";
    private const string SystemNotResolvedReason = "SYSTEM_NOT_RESOLVED";
    private const string GlassNotResolvedReason = "GLASS_NOT_RESOLVED";
    private const string FinishNotResolvedReason = "FINISH_NOT_RESOLVED";
    private const string HistoricalUnavailableReason =
        "HISTORICAL_SIMILARITY_UNAVAILABLE";
    private const string QuantityMissingReason = "QUANTITY_MISSING";

    public async Task<BuildRequirementTechnicalProposalResult> ExecuteAsync(
        Guid requirementId,
        CancellationToken cancellationToken = default)
    {
        var extraction = await requirementRepository.GetLatestSuccessfulExtractionAsync(
            requirementId,
            cancellationToken);
        if (extraction is null)
        {
            return BuildRequirementTechnicalProposalResult.NotFound();
        }

        var items = await requirementRepository.GetExtractedItemsAsync(
            extraction.Id,
            cancellationToken);

        var proposal = await BuildAsync(
            requirementId,
            extraction,
            items,
            cancellationToken);

        requirementRepository.AddTechnicalProposal(proposal);
        await requirementRepository.SaveChangesAsync(cancellationToken);

        return BuildRequirementTechnicalProposalResult.Success(proposal);
    }

    public async Task<RequirementTechnicalProposal> BuildAsync(
        Guid requirementId,
        RequirementExtractionResult extraction,
        IReadOnlyList<RequirementExtractedItem> items,
        CancellationToken cancellationToken = default)
    {
        var systems = await productSystemCatalog.ListActiveSelectableAsync(
            cancellationToken);
        var glasses = await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
            cancellationToken);
        var finishes = await finishCatalog.ListActiveAsync(cancellationToken);
        var createdAtUtc = DateTimeOffset.UtcNow;

        var proposal = RequirementTechnicalProposal.Create(
            requirementId,
            extraction.Id,
            extraction.RequirementProcessingAttemptId,
            false,
            createdAtUtc);

        foreach (var item in items.OrderBy(value => value.Sequence))
        {
            proposal.AddItem(await BuildItemAsync(
                proposal.Id,
                item,
                systems,
                glasses,
                finishes,
                createdAtUtc,
                cancellationToken));
        }

        return proposal;
    }

    private async Task<RequirementTechnicalProposalItem> BuildItemAsync(
        Guid proposalId,
        RequirementExtractedItem item,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        IReadOnlyList<FinishTypeCatalogReadModel> finishes,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var glass = glassResolver.Resolve(item, glasses);
        var finish = finishResolver.Resolve(item, finishes);
        var historical = await historicalTechnicalEvidence.ResolveAsync(
            MapSystemInput(item),
            MapHistoricalQuery(item),
            cancellationToken);

        var systemSelection = historical.Selection;
        var suggestedSystemId = systems.FirstOrDefault(system =>
            system.Code == systemSelection.SuggestedSystemCode)?.Id;
        var suggestedGlassId = glass.Suggested?.GlassTypeId;
        var suggestedFinishId = finish.Suggested?.FinishTypeId;

        var reviewReasons = new HashSet<string>(StringComparer.Ordinal);
        Add(reviewReasons, item.ReviewReasons);
        Add(reviewReasons, systemSelection.ReviewReasons);
        Add(reviewReasons, glass.ReviewReasons);
        Add(reviewReasons, finish.ReviewReasons);

        if (suggestedSystemId is null)
        {
            reviewReasons.Add(SystemNotResolvedReason);
        }

        if (suggestedGlassId is null)
        {
            reviewReasons.Add(GlassNotResolvedReason);
        }

        if (suggestedFinishId is null)
        {
            reviewReasons.Add(FinishNotResolvedReason);
        }

        if (historical.SimilarityStatus == HistoricalSimilarityStatus.TechnicalFailure)
        {
            reviewReasons.Add(HistoricalUnavailableReason);
        }

        if (item.Quantity is null)
        {
            reviewReasons.Add(QuantityMissingReason);
        }

        var systemConfidence = suggestedSystemId is null
            ? 0m
            : systemSelection.Confidence;
        var glassConfidence = suggestedGlassId is null ? 0m : glass.Confidence;
        var finishConfidence = suggestedFinishId is null ? 0m : finish.Confidence;
        var overallConfidence = Math.Min(
            systemConfidence,
            Math.Min(glassConfidence, finishConfidence));
        var technicallyComplete = suggestedSystemId is not null
            && suggestedGlassId is not null
            && suggestedFinishId is not null;
        var requiresReview = item.RequiresReview
            || systemSelection.RequiresReview
            || glass.RequiresReview
            || finish.RequiresReview
            || reviewReasons.Count > 0;

        var proposalItem = RequirementTechnicalProposalItem.Create(
            proposalId,
            item.Id,
            suggestedSystemId,
            suggestedGlassId,
            suggestedFinishId,
            overallConfidence,
            systemConfidence,
            glassConfidence,
            finishConfidence,
            requiresReview,
            technicallyComplete,
            technicallyComplete && item.Quantity is not null,
            reviewReasons.Order(StringComparer.Ordinal),
            SystemResolutionReasons(systemSelection),
            glass.ResolutionReasons,
            finish.ResolutionReasons,
            systemSelection.HistoricalSupportCount,
            systemSelection.HistoricalBestSimilarity,
            systemSelection.HistoricalAverageSimilarity,
            historical.SimilarityStatus.ToString(),
            createdAtUtc);

        AddSystemAlternatives(proposalItem, systemSelection, systems);
        AddGlassAlternatives(proposalItem, glass);
        AddFinishAlternatives(proposalItem, finish);
        AddHistoricalExamples(proposalItem, systemSelection);

        return proposalItem;
    }

    private static SgTechnicalSelectionInput MapSystemInput(
        RequirementExtractedItem item) =>
        new(
            item.FunctionalType,
            item.Operation,
            item.WidthMillimeters,
            item.HeightMillimeters,
            item.AreaSquareMeters,
            item.PanelCount,
            item.MovablePanelCount,
            item.FixedPanelCount,
            item.Modulation,
            item.OpeningDirection,
            item.SpecialFeatures,
            item.GeometryType,
            null,
            item.RequestedSystemRaw ?? item.RequestedProfileRaw,
            item.Arrangement);

    private static HistoricalCandidateQuery MapHistoricalQuery(
        RequirementExtractedItem item) =>
        new(
            Category(item.ElementType),
            item.RequestedSystemRaw ?? item.RequestedProfileRaw,
            item.GlassTypeNormalized ?? item.GlassTypeRaw,
            item.GlassThicknessMm,
            item.Arrangement ?? item.Operation,
            item.WidthMillimeters,
            item.HeightMillimeters,
            item.AreaSquareMeters,
            item.FinishRawDescription ?? item.FinishColorNormalized,
            item.Quantity,
            10,
            GlassComposition: item.GlassComposition);

    private static string? Category(StructuredElementType value) => value switch
    {
        StructuredElementType.Window => "VENTANA",
        StructuredElementType.Door => "PUERTA",
        StructuredElementType.Facade => "FACHADA",
        StructuredElementType.Partition => "DIVISION",
        StructuredElementType.Railing => "BARANDA",
        StructuredElementType.Skylight => "LUCERNARIO",
        StructuredElementType.ShowerDivision => "DIVISION_BANO",
        _ => null
    };

    private static IReadOnlyList<string> SystemResolutionReasons(
        SgTechnicalSelectionResult selection)
    {
        var reasons = new List<string> { selection.AppliedRuleCode };
        if (selection.HistoricalSupportCount > 0)
        {
            reasons.Add("SYSTEM_HISTORICAL_SUPPORT");
        }

        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddSystemAlternatives(
        RequirementTechnicalProposalItem item,
        SgTechnicalSelectionResult selection,
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
    {
        var rank = 1;
        foreach (var alternative in selection.Alternatives)
        {
            var system = systems.FirstOrDefault(value => value.Code == alternative);
            if (system is null || system.Id == item.SuggestedSystemId)
            {
                continue;
            }

            item.AddSystemAlternative(
                RequirementTechnicalProposalSystemAlternative.Create(
                    item.Id,
                    system.Id,
                    rank++,
                    0m,
                    [SystemAlternativeReason]));
        }
    }

    private static void AddGlassAlternatives(
        RequirementTechnicalProposalItem item,
        GlassCandidateResolutionResult glass)
    {
        var rank = 1;
        foreach (var alternative in glass.Alternatives)
        {
            if (alternative.GlassTypeId == item.SuggestedGlassTypeId)
            {
                continue;
            }

            item.AddGlassAlternative(
                RequirementTechnicalProposalGlassAlternative.Create(
                    item.Id,
                    alternative.GlassTypeId,
                    rank++,
                    alternative.Confidence,
                    alternative.Reasons));
        }
    }

    private static void AddFinishAlternatives(
        RequirementTechnicalProposalItem item,
        FinishCandidateResolutionResult finish)
    {
        var rank = 1;
        foreach (var alternative in finish.Alternatives)
        {
            if (alternative.FinishTypeId == item.SuggestedFinishTypeId)
            {
                continue;
            }

            item.AddFinishAlternative(
                RequirementTechnicalProposalFinishAlternative.Create(
                    item.Id,
                    alternative.FinishTypeId,
                    rank++,
                    alternative.Confidence,
                    alternative.Reasons));
        }
    }

    private static void AddHistoricalExamples(
        RequirementTechnicalProposalItem item,
        SgTechnicalSelectionResult selection)
    {
        foreach (var example in selection.HistoricalExamples ?? [])
        {
            item.AddHistoricalExample(
                RequirementTechnicalProposalHistoricalExample.Create(
                    item.Id,
                    example.CandidateId,
                    example.QuoteId,
                    example.HistoricalReference,
                    example.SimilarityScore,
                    example.MatchedFeatures,
                    example.Differences,
                    example.TechnicalExplanation));
        }
    }

    private static void Add(ISet<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value.Trim());
            }
        }
    }
}

public sealed record BuildRequirementTechnicalProposalResult(
    bool IsSuccess,
    RequirementTechnicalProposal? Proposal)
{
    public static BuildRequirementTechnicalProposalResult Success(
        RequirementTechnicalProposal proposal) =>
        new(true, proposal);

    public static BuildRequirementTechnicalProposalResult NotFound() =>
        new(false, null);
}
