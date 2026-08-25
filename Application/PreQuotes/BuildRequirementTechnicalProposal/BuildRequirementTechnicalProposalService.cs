using System.Diagnostics;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Diagnostics;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.PreQuotes.BuildRequirementTechnicalProposal;

public sealed class BuildRequirementTechnicalProposalService(
    IRequirementRepository requirementRepository,
    IProductSystemCatalogRepository productSystemCatalog,
    IGlassTypeCatalogRepository glassCatalog,
    IFinishTypeCatalogRepository finishCatalog,
    IGlassCandidateResolver glassResolver,
    IFinishCandidateResolver finishResolver,
    ResolveHistoricalTechnicalEvidenceService historicalTechnicalEvidence,
    ILogger<BuildRequirementTechnicalProposalService>? logger = null)
{
    private readonly ILogger<BuildRequirementTechnicalProposalService> _logger =
        logger ?? NullLogger<BuildRequirementTechnicalProposalService>.Instance;
    private const string SystemAlternativeReason = "SYSTEM_ALTERNATIVE";
    private const string SystemNotResolvedReason = "SYSTEM_NOT_RESOLVED";
    private const string GlassNotResolvedReason = "GLASS_NOT_RESOLVED";
    private const string FinishNotResolvedReason = "FINISH_NOT_RESOLVED";
    private const string HistoricalDefaultGlassReason =
        "HISTORICAL_DEFAULT_GLASS";
    private const string HistoricalDefaultFinishReason =
        "HISTORICAL_DEFAULT_FINISH";
    private const string QuantityMissingReason = "QUANTITY_MISSING";
    private const string DefaultGlassCode = "TEMP_5";
    private const string DefaultFinishCommercialCode = "PP13";
    private const decimal DefaultGlassConfidence = 0.60m;
    private const decimal DefaultFinishConfidence = 0.70m;

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

        var requirement = await requirementRepository.FindByIdAsync(
            requirementId,
            cancellationToken);
        if (requirement?.CommercialLine is null)
        {
            return BuildRequirementTechnicalProposalResult.NotFound();
        }

        var items = await requirementRepository.GetExtractedItemsAsync(
            extraction.Id,
            cancellationToken);

        var proposal = await BuildAsync(
            requirementId,
            requirement.CommercialLine.Value,
            extraction,
            items,
            cancellationToken);

        requirementRepository.AddTechnicalProposal(proposal);
        await requirementRepository.SaveChangesAsync(cancellationToken);

        return BuildRequirementTechnicalProposalResult.Success(proposal);
    }

    public async Task<RequirementTechnicalProposal> BuildAsync(
        Guid requirementId,
        RequirementCommercialLine commercialLine,
        RequirementExtractionResult extraction,
        IReadOnlyList<RequirementExtractedItem> items,
        CancellationToken cancellationToken = default)
    {
        var catalogs = Stopwatch.StartNew();
        var systems = await productSystemCatalog.ListActiveSelectableAsync(
            cancellationToken);
        var glasses = await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
            cancellationToken);
        var finishes = await finishCatalog.ListActiveAsync(cancellationToken);
        LogPerf(
            requirementId,
            extraction.RequirementProcessingAttemptId,
            "LOAD_CATALOGS",
            catalogs,
            ("systemCount", systems.Count),
            ("glassCount", glasses.Count),
            ("finishCount", finishes.Count));
        var createdAtUtc = DateTimeOffset.UtcNow;

        var proposal = RequirementTechnicalProposal.Create(
            requirementId,
            extraction.Id,
            extraction.RequirementProcessingAttemptId,
            false,
            createdAtUtc);

        var orderedItems = items.OrderBy(value => value.Sequence).ToArray();
        var historicalRequests = orderedItems.Select(item =>
                new HistoricalTechnicalEvidenceBatchRequest(
                item.Id,
                MapSystemInput(item, commercialLine),
                MapHistoricalQuery(item))).ToArray();
        var historicalByItemId = await historicalTechnicalEvidence.ResolveBatchAsync(
            historicalRequests,
            cancellationToken);

        foreach (var item in orderedItems)
        {
            var itemStopwatch = Stopwatch.StartNew();
            proposal.AddItem(BuildItem(
                proposal.Id,
                item,
                commercialLine,
                historicalByItemId[item.Id],
                systems,
                glasses,
                finishes,
                createdAtUtc));
            LogPerf(
                requirementId,
                extraction.RequirementProcessingAttemptId,
                "BUILD_FUNCTIONAL_INTERPRETATION",
                itemStopwatch,
                ("itemSequence", item.Sequence),
                ("reference", item.Reference));
        }

        return proposal;
    }

    private RequirementTechnicalProposalItem BuildItem(
        Guid proposalId,
        RequirementExtractedItem item,
        RequirementCommercialLine commercialLine,
        HistoricalTechnicalEvidenceSelectionResult historical,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        IReadOnlyList<FinishTypeCatalogReadModel> finishes,
        DateTimeOffset createdAtUtc)
    {
        var glass = glassResolver.Resolve(item, glasses);
        var finish = finishResolver.Resolve(item, finishes);

        var systemSelection = historical.Selection;
        var suggestedSystem = systems.FirstOrDefault(system =>
            system.Code == systemSelection.SuggestedSystemCode);
        var suggestedSystemId = suggestedSystem?.Id;
        glass = ApplyConfirmedGlassRules(
                item,
                commercialLine,
                suggestedSystem,
                glasses)
            ?? glass;
        var defaultGlass = glass.Suggested is null
            ? FindDefaultGlass(glasses)
            : null;
        var defaultFinish = finish.Suggested is null
            && !SystemSkipsFinishDefault(suggestedSystem)
                ? FindDefaultFinish(finishes)
                : null;
        var suggestedGlassId = glass.Suggested?.GlassTypeId
            ?? defaultGlass?.GlassTypeId;
        var suggestedFinishId = finish.Suggested?.FinishTypeId
            ?? defaultFinish?.Id;

        var reviewReasons = new HashSet<string>(StringComparer.Ordinal);
        Add(reviewReasons, item.ReviewReasons);
        Add(reviewReasons, systemSelection.ReviewReasons);
        if (defaultGlass is null)
        {
            Add(reviewReasons, glass.ReviewReasons);
        }
        else
        {
            reviewReasons.Add(HistoricalDefaultGlassReason);
        }

        if (defaultFinish is null)
        {
            Add(reviewReasons, finish.ReviewReasons);
        }

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

        if (item.Quantity is null)
        {
            reviewReasons.Add(QuantityMissingReason);
        }

        var systemConfidence = suggestedSystemId is null
            ? 0m
            : systemSelection.Confidence;
        var glassConfidence = suggestedGlassId is null
            ? 0m
            : defaultGlass is null ? glass.Confidence : DefaultGlassConfidence;
        var finishConfidence = suggestedFinishId is null
            ? 0m
            : defaultFinish is null ? finish.Confidence : DefaultFinishConfidence;
        var overallConfidence = Math.Min(
            systemConfidence,
            Math.Min(glassConfidence, finishConfidence));
        var technicallyComplete = suggestedSystemId is not null
            && suggestedGlassId is not null
            && suggestedFinishId is not null;
        var requiresReview = item.RequiresReview
            || systemSelection.RequiresReview
            || (defaultGlass is null ? glass.RequiresReview : true)
            || (defaultFinish is null && finish.RequiresReview)
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
            defaultGlass is null
                ? glass.ResolutionReasons
                : [HistoricalDefaultGlassReason],
            defaultFinish is null
                ? finish.ResolutionReasons
                : [HistoricalDefaultFinishReason],
            historical.EvidenceStatus.SupportCount,
            historical.EvidenceStatus.BestSimilarity,
            historical.EvidenceStatus.AverageSimilarity,
            historical.EvidenceStatus.Status,
            createdAtUtc);

        AddSystemAlternatives(proposalItem, systemSelection, systems);
        AddGlassAlternatives(proposalItem, glass);
        AddFinishAlternatives(proposalItem, finish);
        AddHistoricalExamples(proposalItem, systemSelection);

        return proposalItem;
    }

    private static GlassTypeCatalogReadModel? FindDefaultGlass(
        IReadOnlyList<GlassTypeCatalogReadModel> glasses) =>
        glasses.FirstOrDefault(value =>
            value.IsActive
            && value.IsSelectable
            && value.Code.Equals(
                DefaultGlassCode,
                StringComparison.OrdinalIgnoreCase));

    private static GlassCandidateResolutionResult? ApplyConfirmedGlassRules(
        RequirementExtractedItem item,
        RequirementCommercialLine commercialLine,
        ProductSystemCatalogReadModel? suggestedSystem,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        if (IsExplicitSpecialGlassVariant(item))
        {
            return null;
        }

        if (IsShowerDivision(item))
        {
            return RuleGlass(
                glasses,
                "TEMP_8",
                [GlassResolutionReasonCodes.SpecialGlassShower8Mm]);
        }

        if (IsRailingOrGuardrail(item))
        {
            return RuleGlass(
                glasses,
                "TEMP_10",
                [GlassResolutionReasonCodes.SpecialGlassRailing10Mm]);
        }

        var line = ToSelectorLine(commercialLine)
            ?? CommercialLine(suggestedSystem);
        if (line is "CLASSIC" or "ESSENTIAL" or "PREMIUM")
        {
            return TemperedRuleGlass(item, glasses);
        }

        if (line is "BIOCONFORT" or "SIGNATURE")
        {
            return LaminatedRuleGlass(item, glasses);
        }

        return null;
    }

    private static GlassCandidateResolutionResult? TemperedRuleGlass(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        if (item.WidthMillimeters is not { } width
            || item.HeightMillimeters is not { } height)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineTempered
        };

        if (width > 1950)
        {
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
            return RuleGlass(glasses, "TEMP_10", reasons);
        }

        var narrow = width <= 500;
        var code = height switch
        {
            <= 2400 => "TEMP_5",
            <= 2600 when narrow => "TEMP_5",
            <= 2600 => "TEMP_6",
            <= 2800 when narrow => "TEMP_6",
            <= 2800 => "TEMP_8",
            <= 3000 when narrow => "TEMP_8",
            _ => "TEMP_10"
        };

        if (narrow && height > 2400 && height <= 3000)
        {
            reasons.Add(GlassResolutionReasonCodes.NarrowGlassHeightExtension);
        }

        return RuleGlass(glasses, code, reasons);
    }

    private static GlassCandidateResolutionResult? LaminatedRuleGlass(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        if (item.WidthMillimeters is not { } width
            || item.HeightMillimeters is not { } height)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineLaminated
        };
        var code = "LAM_4_4";
        if (width > 1950 && height > 2800)
        {
            code = "LAM_5_5";
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
            reasons.Add(GlassResolutionReasonCodes.Laminated55JointAndHeight);
        }

        return RuleGlass(glasses, code, reasons);
    }

    private static GlassCandidateResolutionResult? RuleGlass(
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        string code,
        IReadOnlyList<string> reasons)
    {
        var glass = glasses.FirstOrDefault(value =>
            value.IsActive
            && value.IsSelectable
            && value.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (glass is null)
        {
            return null;
        }

        var alternative = new GlassCandidateAlternative(
            glass.GlassTypeId,
            glass.Code,
            glass.Name,
            1m,
            reasons);
        return new GlassCandidateResolutionResult(
            alternative,
            [alternative],
            1m,
            false,
            [],
            reasons);
    }

    private static FinishTypeCatalogReadModel? FindDefaultFinish(
        IReadOnlyList<FinishTypeCatalogReadModel> finishes) =>
        finishes.FirstOrDefault(value =>
            value.IsActive
            && value.IsSelectable
            && (value.CommercialCode?.Equals(
                    DefaultFinishCommercialCode,
                    StringComparison.OrdinalIgnoreCase) == true
                || value.Code.Equals(
                    DefaultFinishCommercialCode,
                    StringComparison.OrdinalIgnoreCase)
                || value.Code.Contains(
                    DefaultFinishCommercialCode,
                    StringComparison.OrdinalIgnoreCase)
                || value.Name.Contains(
                    DefaultFinishCommercialCode,
                    StringComparison.OrdinalIgnoreCase)));

    private static bool SystemSkipsFinishDefault(
        ProductSystemCatalogReadModel? system)
    {
        if (system is null)
        {
            return false;
        }

        return Contains(system.Code, "INOX")
            || Contains(system.Name, "INOX")
            || Contains(system.TechnicalName, "INOX")
            || Contains(system.CommercialName, "INOX")
            || Contains(system.Family, "INOX")
            || Contains(system.Variant, "INOX")
            || IsNotApplicable(system.Code)
            || IsNotApplicable(system.Name);
    }

    private static string? CommercialLine(ProductSystemCatalogReadModel? system)
    {
        if (string.IsNullOrWhiteSpace(system?.CommercialLine))
        {
            return null;
        }

        return system.CommercialLine.Trim().ToUpperInvariant();
    }

    private static bool IsShowerDivision(RequirementExtractedItem item) =>
        item.ElementType == StructuredElementType.ShowerDivision
        || Contains(item.FunctionalType, "SHOWER_DIVISION")
        || Contains(item.FunctionalType, "BATHROOM_DIVISION")
        || Contains(item.Description, "DIVISION DE BANO")
        || Contains(item.Description, "DIVISION DE BAÑO");

    private static bool IsRailingOrGuardrail(RequirementExtractedItem item) =>
        item.ElementType == StructuredElementType.Railing
        || Contains(item.FunctionalType, "RAILING")
        || Contains(item.FunctionalType, "BARANDA")
        || Contains(item.Description, "BARANDA")
        || Contains(item.Description, "ESCALERA")
        || Contains(item.Description, "PASAMANOS");

    private static bool IsExplicitSpecialGlassVariant(
        RequirementExtractedItem item) =>
        Contains(item.GlassRawSpecification, "GRIS")
        || Contains(item.GlassRawSpecification, "GRAY")
        || Contains(item.GlassRawSpecification, "QUALITY GLASS")
        || Contains(item.GlassRawSpecification, "CL120")
        || Contains(item.GlassRawSpecification, "CL150")
        || Contains(item.GlassRawSpecification, "CL167")
        || Contains(item.GlassTypeRaw, "GRIS")
        || Contains(item.GlassTypeRaw, "GRAY")
        || Contains(item.GlassTypeRaw, "QUALITY GLASS")
        || Contains(item.GlassTypeNormalized, "GRIS")
        || Contains(item.GlassTypeNormalized, "GRAY")
        || Contains(item.GlassTypeNormalized, "QUALITY GLASS");

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsNotApplicable(string? value) =>
        value?.Trim().Equals("N.A.", StringComparison.OrdinalIgnoreCase) == true
        || value?.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase) == true;

    private static SgTechnicalSelectionInput MapSystemInput(
        RequirementExtractedItem item,
        RequirementCommercialLine commercialLine) =>
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
            ToSelectorLine(commercialLine),
            item.RequestedSystemRaw ?? item.RequestedProfileRaw,
            item.Arrangement);

    private static string? ToSelectorLine(RequirementCommercialLine commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => "CLASSIC",
            RequirementCommercialLine.Essential => "ESSENTIAL",
            RequirementCommercialLine.Bioconfort => "BIOCONFORT",
            RequirementCommercialLine.Signature => "SIGNATURE",
            _ => null
        };

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
        reasons.AddRange(selection.ResolutionReasons ?? []);
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

    private void LogPerf(
        Guid requirementId,
        Guid attemptId,
        string stage,
        Stopwatch stopwatch,
        params (string Name, object? Value)[] values)
    {
        stopwatch.Stop();
        var context = NewPipePerformanceContext.Current;
        var detail = string.Join(
            " ",
            values.Select(value => $"{value.Name}={value.Value}"));
        _logger.LogInformation(
            "[NEWPIPE-PERF] RequirementId={RequirementId} AttemptId={AttemptId} Stage={Stage} ElapsedMs={ElapsedMs} SimilarityCallCount={SimilarityCallCount} SimilarityCandidateCountTotal={SimilarityCandidateCountTotal} CorpusReloadCount={CorpusReloadCount} {Detail}",
            requirementId,
            attemptId,
            stage,
            stopwatch.ElapsedMilliseconds,
            context?.SimilarityCallCount ?? 0,
            context?.SimilarityCandidateCountTotal ?? 0,
            context?.CorpusReloadCount ?? 0,
            detail);
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
