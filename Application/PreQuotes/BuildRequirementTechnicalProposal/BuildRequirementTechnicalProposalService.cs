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
    private const int GlassPaneSegmentationToleranceMillimeters = 10;
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
        var systemContext = ResolveSystemSelectionContext(item, commercialLine);
        var primaryComponent = systemContext.PrimaryComponent;

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
        Add(reviewReasons, primaryComponent.ReviewReasons);
        Add(reviewReasons, systemContext.ReviewReasons);
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
        var geometry = GlassPaneGeometry.Resolve(item);
        if (geometry is null)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineTempered
        };
        reasons.AddRange(geometry.ResolutionReasons);
        var reviewReasons = new List<string>();
        if (geometry.RequiresReview)
        {
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneGeometryUnresolved);
        }

        var paneCodes = new List<string>();
        var hasJoint = false;
        foreach (var pane in geometry.Panes)
        {
            if (geometry.CanEvaluateJoint && pane.WidthMm > 1950)
            {
                hasJoint = true;
                paneCodes.Add("TEMP_10");
                continue;
            }

            var narrow = pane.WidthMm <= 500;
            paneCodes.Add(pane.HeightMm switch
            {
                <= 2400 => "TEMP_5",
                <= 2600 when narrow => "TEMP_5",
                <= 2600 => "TEMP_6",
                <= 2800 when narrow => "TEMP_6",
                <= 2800 => "TEMP_8",
                <= 3000 when narrow => "TEMP_8",
                _ => "TEMP_10"
            });

            if (narrow && pane.HeightMm > 2400 && pane.HeightMm <= 3000)
            {
                reasons.Add(GlassResolutionReasonCodes
                    .NarrowGlassHeightExtension);
            }
        }

        if (hasJoint)
        {
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
        }

        var code = paneCodes
            .OrderByDescending(TemperedRank)
            .First();
        if (paneCodes.Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            reasons.Add(GlassResolutionReasonCodes
                .GlassPaneHeterogeneousNeeds);
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneHeterogeneousNeeds);
        }

        return RuleGlass(glasses, code, reasons, reviewReasons);
    }

    private static GlassCandidateResolutionResult? LaminatedRuleGlass(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        var geometry = GlassPaneGeometry.Resolve(item);
        if (geometry is null)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineLaminated
        };
        reasons.AddRange(geometry.ResolutionReasons);
        var reviewReasons = new List<string>();
        if (geometry.RequiresReview)
        {
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneGeometryUnresolved);
        }

        var code = "LAM_4_4";
        if (geometry.CanEvaluateJoint
            && geometry.Panes.Any(pane => pane.WidthMm > 1950
                && pane.HeightMm > 2800))
        {
            code = "LAM_5_5";
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
            reasons.Add(GlassResolutionReasonCodes.Laminated55JointAndHeight);
        }

        return RuleGlass(glasses, code, reasons, reviewReasons);
    }

    private static GlassCandidateResolutionResult? RuleGlass(
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        string code,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string>? reviewReasons = null)
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
            reviewReasons?.Count > 0,
            reviewReasons ?? [],
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static int TemperedRank(string code) => code switch
    {
        "TEMP_10" => 4,
        "TEMP_8" => 3,
        "TEMP_6" => 2,
        _ => 1
    };

    private sealed record GlassPane(int WidthMm, int HeightMm);

    private sealed record GlassPaneGeometry(
        IReadOnlyList<GlassPane> Panes,
        IReadOnlyList<string> ResolutionReasons,
        bool RequiresReview,
        bool CanEvaluateJoint)
    {
        public static GlassPaneGeometry? Resolve(RequirementExtractedItem item)
        {
            var isRoofGlass = IsRoofGlassDomain(item);
            var isPocket = IsPocketDomain(item);
            if (item.WidthMillimeters is not { } width
                || item.HeightMillimeters is not { } height)
            {
                return TryResolveExplicitSegments(item)
                    ?? TryResolvePocketLeafGeometry(
                        item,
                        item.HeightMillimeters)
                    ?? TryResolveRoofGeometry(item);
            }

            var explicitSegments = TryResolveExplicitSegments(item);
            if (explicitSegments is not null)
            {
                return explicitSegments;
            }

            var explicitHeights = TryResolveExplicitHorizontalPanes(
                item,
                width,
                height);
            if (explicitHeights is not null)
            {
                return explicitHeights;
            }

            var explicitWidths = TryResolveExplicitVerticalPanes(
                item,
                width,
                height);
            if (explicitWidths is not null)
            {
                return explicitWidths;
            }

            var pocketLeafGeometry = TryResolvePocketLeafGeometry(
                item,
                height);
            if (pocketLeafGeometry is not null)
            {
                return pocketLeafGeometry;
            }

            var evidencePanes = TryResolveEvidencePanes(
                item,
                width,
                height);
            if (evidencePanes is not null)
            {
                return evidencePanes;
            }

            var roofGeometry = TryResolveRoofGeometry(item);
            if (roofGeometry is not null)
            {
                return roofGeometry;
            }

            if (isRoofGlass)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                    true,
                    false);
            }

            if (isPocket)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                    true,
                    false);
            }

            if (item.PanelCount == 1)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromElement],
                    false,
                    true);
            }

            return new GlassPaneGeometry(
                [new GlassPane(width, height)],
                [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                item.PanelCount is > 1 || width > 1950,
                false);
        }

        private static GlassPaneGeometry? TryResolveRoofGeometry(
            RequirementExtractedItem item)
        {
            if (!IsRoofGlassDomain(item)
                || item.PanelCount is > 1)
            {
                return null;
            }

            foreach (var text in AssociatedEvidenceTexts(item))
            {
                if (TryExtractRoofLengthWidth(text) is not { } dimensions)
                {
                    continue;
                }

                var paneWidth = Math.Min(
                    dimensions.LengthMm,
                    dimensions.WidthMm);
                var paneHeight = Math.Max(
                    dimensions.LengthMm,
                    dimensions.WidthMm);
                return new GlassPaneGeometry(
                    [new GlassPane(paneWidth, paneHeight)],
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromRoofGeometry],
                    false,
                    true);
            }

            return null;
        }

        private static GlassPaneGeometry? TryResolvePocketLeafGeometry(
            RequirementExtractedItem item,
            int? height)
        {
            if (!IsPocketDomain(item)
                || height is not { } paneHeight)
            {
                return null;
            }

            foreach (var text in AssociatedEvidenceTexts(item))
            {
                var repeatedLeafPanes = TryExtractRepeatedPocketLeafPanes(
                    text,
                    paneHeight);
                if (repeatedLeafPanes is not null)
                {
                    return repeatedLeafPanes;
                }

                if (TryExtractPocketLeafWidth(text) is not { } leafWidth)
                {
                    continue;
                }

                return new GlassPaneGeometry(
                    [new GlassPane(leafWidth, paneHeight)],
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromPocketLeaf],
                    false,
                    true);
            }

            return null;
        }

        private static GlassPaneGeometry? TryExtractRepeatedPocketLeafPanes(
            string text,
            int height)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\b(?<count>\d{1,2})\s*(?:hojas|leafs|leaves|naves|panel(?:es)?\s+m[oÃ³]vil(?:es)?)\s*(?:de|=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var count = int.Parse(
                match.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (count is <= 1 or > 20)
            {
                return null;
            }

            var width = ToMillimeters(
                match.Groups["value"].Value,
                match.Groups["unit"].Value);
            return new GlassPaneGeometry(
                Enumerable.Repeat(new GlassPane(width, height), count).ToArray(),
                [GlassResolutionReasonCodes.GlassPaneDimensionsFromPocketLeaf],
                false,
                true);
        }

        private static int? TryExtractPocketLeafWidth(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\b(?:(?:ancho|width)\s+(?:de\s+)?(?:hoja|leaf|nave|panel\s+m[oÃ³]vil|abertura|opening|vano\s+[uÃº]til|luz)|(?:hoja|leaf|nave|panel\s+m[oÃ³]vil|abertura|opening|vano\s+[uÃº]til|luz)\s+(?:width|ancho))\b\s*(?:=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value)
                : null;
        }

        private static bool IsPocketDomain(RequirementExtractedItem item) =>
            AssociatedPocketContextTexts(item)
                .Any(text =>
                    Contains(text, "POCKET")
                    || Contains(text, "BOLSILLO"));

        private static IEnumerable<string?> AssociatedPocketContextTexts(
            RequirementExtractedItem item)
        {
            yield return item.Description;
            yield return item.FunctionalType;
            yield return item.Operation;
            yield return item.Arrangement;
            yield return item.Modulation;
            yield return item.OpeningDirection;
            yield return item.GeometryType;
            yield return item.AssemblyType;
            foreach (var feature in item.SpecialFeatures)
            {
                yield return feature;
            }

            foreach (var text in item.Evidence.Select(evidence => evidence.Text))
            {
                yield return text;
            }

            foreach (var segment in item.Segments)
            {
                yield return segment.Role;
                yield return segment.Operation;
                yield return segment.GeometryType;
                yield return segment.EvidenceText;
            }
        }

        private static bool IsRoofGlassDomain(RequirementExtractedItem item) =>
            item.ElementType == StructuredElementType.Skylight
            || Contains(item.FunctionalType, "SKYLIGHT")
            || Contains(item.FunctionalType, "CLARABOYA")
            || Contains(item.FunctionalType, "TECHO EN VIDRIO")
            || Contains(item.FunctionalType, "CUBIERTA EN VIDRIO")
            || Contains(item.FunctionalType, "ROOF GLASS")
            || Contains(item.FunctionalType, "GLASS ROOF")
            || Contains(item.Description, "CLARABOYA")
            || Contains(item.Description, "TECHO EN VIDRIO")
            || Contains(item.Description, "CUBIERTA EN VIDRIO")
            || Contains(item.Description, "ROOF GLASS")
            || Contains(item.Description, "GLASS ROOF");

        private static RoofGlassDimensions? TryExtractRoofLengthWidth(
            string text)
        {
            var length = TryExtractLabeledDimension(
                text,
                "(?:largo|length)");
            var width = TryExtractLabeledDimension(
                text,
                "(?:ancho|width)");
            return length is { } lengthMm && width is { } widthMm
                ? new RoofGlassDimensions(lengthMm, widthMm)
                : null;
        }

        private static int? TryExtractLabeledDimension(
            string text,
            string labelPattern)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                $@"\b{labelPattern}\b\s*(?:=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value)
                : null;
        }

        private static GlassPaneGeometry? TryResolveExplicitHorizontalPanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            var source = string.Join(
                " ",
                item.Modulation,
                item.Arrangement);
            if (!Contains(source, "HEIGHT")
                && !Contains(source, "ALTO")
                && !Contains(source, "HORIZONTAL"))
            {
                return null;
            }

            var heights = System.Text.RegularExpressions.Regex
                .Matches(source, @"\d{2,5}")
                .Select(match => int.Parse(
                    match.Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => value > 0)
                .ToArray();
            if (heights.Length < 2 || heights.Sum() != height)
            {
                return null;
            }

            return new GlassPaneGeometry(
                heights.Select(value => new GlassPane(width, value)).ToArray(),
                [GlassResolutionReasonCodes
                    .GlassPaneDimensionsFromSubmodules],
                false,
                true);
        }

        private static GlassPaneGeometry? TryResolveExplicitSegments(
            RequirementExtractedItem item)
        {
            var panes = item.Segments
                .Where(segment =>
                    segment.WidthMillimeters is > 0
                    && segment.HeightMillimeters is > 0)
                .Select(segment => new GlassPane(
                    segment.WidthMillimeters!.Value,
                    segment.HeightMillimeters!.Value))
                .ToArray();
            return panes.Length == 0
                ? null
                : new GlassPaneGeometry(
                    panes,
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromSubmodules],
                    false,
                    true);
        }

        private static GlassPaneGeometry? TryResolveExplicitVerticalPanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            var source = string.Join(
                " ",
                item.Modulation,
                item.Arrangement);
            if (!Contains(source, "WIDTH")
                && !Contains(source, "ANCHO")
                && !Contains(source, "VERTICAL"))
            {
                return null;
            }

            var widths = System.Text.RegularExpressions.Regex
                .Matches(source, @"\d{2,5}")
                .Select(match => int.Parse(
                    match.Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => value > 0)
                .ToArray();
            if (widths.Length < 2 || widths.Sum() != width)
            {
                return null;
            }

            return new GlassPaneGeometry(
                widths.Select(value => new GlassPane(value, height)).ToArray(),
                [GlassResolutionReasonCodes
                    .GlassPaneDimensionsFromSubmodules],
                false,
                    true);
        }

        private static GlassPaneGeometry? TryResolveEvidencePanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            foreach (var text in AssociatedEvidenceTexts(item))
            {
                var panes = TryResolveEvidencePanes(text, width, height);
                if (panes is not null)
                {
                    return panes;
                }
            }

            return null;
        }

        private static IEnumerable<string> AssociatedEvidenceTexts(
            RequirementExtractedItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Modulation))
            {
                yield return item.Modulation;
            }

            if (!string.IsNullOrWhiteSpace(item.Arrangement))
            {
                yield return item.Arrangement;
            }

            foreach (var evidence in item.Evidence)
            {
                if (!string.IsNullOrWhiteSpace(evidence.Text))
                {
                    yield return evidence.Text;
                }
            }

            foreach (var segment in item.Segments)
            {
                if (!string.IsNullOrWhiteSpace(segment.EvidenceText))
                {
                    yield return segment.EvidenceText;
                }
            }
        }

        private static GlassPaneGeometry? TryResolveEvidencePanes(
            string text,
            int width,
            int height)
        {
            var dimensions = ExtractExplicitDimensions(text);
            if (dimensions.Length == 0)
            {
                return null;
            }

            var expanded = ExpandRepeatedDimensions(text, dimensions);
            var orientation = ResolveSegmentationOrientation(text, expanded, width, height);
            return orientation switch
            {
                PaneSegmentationOrientation.Vertical => new GlassPaneGeometry(
                    expanded.Select(value => new GlassPane(width, value)).ToArray(),
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence],
                    false,
                    true),
                PaneSegmentationOrientation.Horizontal => new GlassPaneGeometry(
                    expanded.Select(value => new GlassPane(value, height)).ToArray(),
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence],
                    false,
                    true),
                _ => null
            };
        }

        private static int[] ExtractExplicitDimensions(string text) =>
            System.Text.RegularExpressions.Regex
                .Matches(
                    text,
                    @"(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(match => ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value))
                .Where(value => value > 0)
                .ToArray();

        private static int[] ExpandRepeatedDimensions(
            string text,
            int[] dimensions)
        {
            if (dimensions.Length != 1
                || !Contains(text, "CADA UNO"))
            {
                if (dimensions.Length < 2
                    || !Contains(text, "CADA UNO"))
                {
                    return dimensions;
                }
            }

            var count = System.Text.RegularExpressions.Regex
                .Match(
                    text,
                    @"(?<count>\d{1,2})\s*(tramos|panos|paÃ±os|paños|cuerpos|segmentos)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!count.Success)
            {
                return dimensions;
            }

            var value = int.Parse(
                count.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return value is > 1 and <= 20
                ? Enumerable.Repeat(dimensions[^1], value).ToArray()
                : dimensions;
        }

        private static PaneSegmentationOrientation ResolveSegmentationOrientation(
            string text,
            int[] dimensions,
            int width,
            int height)
        {
            if (dimensions.Length < 2)
            {
                return PaneSegmentationOrientation.Unknown;
            }

            var verticalHint = Contains(text, "TRAMO SUPERIOR")
                || Contains(text, "SUPERIOR")
                || Contains(text, "CENTRAL")
                || Contains(text, "INFERIOR")
                || Contains(text, "ALTURA")
                || Contains(text, "ALTO")
                || Contains(text, "VERTICAL");
            var horizontalHint = Contains(text, "ANCHO")
                || Contains(text, "WIDTH")
                || Contains(text, "HORIZONTAL");

            var verticalMatches = SumMatches(dimensions, height);
            var horizontalMatches = SumMatches(dimensions, width);
            if (verticalHint && verticalMatches)
            {
                return PaneSegmentationOrientation.Vertical;
            }

            if (horizontalHint && horizontalMatches)
            {
                return PaneSegmentationOrientation.Horizontal;
            }

            if (verticalMatches && !horizontalMatches)
            {
                return PaneSegmentationOrientation.Vertical;
            }

            if (horizontalMatches && !verticalMatches)
            {
                return PaneSegmentationOrientation.Horizontal;
            }

            return PaneSegmentationOrientation.Unknown;
        }

        private static bool SumMatches(int[] values, int expected) =>
            Math.Abs(values.Sum() - expected)
            <= GlassPaneSegmentationToleranceMillimeters;

        private static int ToMillimeters(string value, string unit)
        {
            var number = decimal.Parse(
                value.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture);
            var multiplier = unit.Trim().ToUpperInvariant() switch
            {
                "M" => 1000m,
                "CM" => 10m,
                _ => 1m
            };

            return (int)Math.Round(number * multiplier, MidpointRounding.AwayFromZero);
        }

        private enum PaneSegmentationOrientation
        {
            Unknown,
            Vertical,
            Horizontal
        }

        private sealed record RoofGlassDimensions(int LengthMm, int WidthMm);
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
        RequirementCommercialLine commercialLine)
        => ResolveSystemSelectionContext(item, commercialLine).Input;

    private static SystemSelectionContext ResolveSystemSelectionContext(
        RequirementExtractedItem item,
        RequirementCommercialLine commercialLine)
    {
        var primary = ResolvePrimaryComponent(item);
        var functionalType = primary.OverridesItem
            ? primary.FunctionalType
            : item.FunctionalType;
        var operation = primary.OverridesItem
            ? primary.Operation
            : item.Operation;

        var geometry = ResolvePrimaryComponentGeometry(item, primary);
        var widthMillimeters = geometry.WidthMillimeters ?? item.WidthMillimeters;
        var heightMillimeters = geometry.HeightMillimeters ?? item.HeightMillimeters;
        var reviewReasons = new List<string>();
        if (geometry.IsUnresolved
            && IsDimensionDependentFunctionalClassification(functionalType, operation))
        {
            widthMillimeters = null;
            heightMillimeters = null;
            reviewReasons.Add(SgTechnicalSelectionReviewReasons.PrimaryComponentGeometryUnresolved);
        }

        var input = new SgTechnicalSelectionInput(
            functionalType,
            operation,
            widthMillimeters,
            heightMillimeters,
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
            item.Arrangement,
            null,
            item.Description,
            primary.OverridesItem && !geometry.IsUnresolved
                ? geometry.HeightMillimeters
                : null,
            item.Segments.Count > 1 || geometry.IsUnresolved,
            AssociatedSystemContextTexts(item));

        return new(input, primary, geometry, reviewReasons);
    }

    private static IReadOnlyList<string> AssociatedSystemContextTexts(
        RequirementExtractedItem item)
    {
        var values = new List<string?>
        {
            item.Description,
            item.FunctionalType,
            item.Operation,
            item.Arrangement,
            item.Modulation,
            item.OpeningDirection,
            item.GeometryType,
            item.AssemblyType,
            item.RequestedSystemRaw,
            item.RequestedProfileRaw
        };

        values.AddRange(item.SpecialFeatures);
        values.AddRange(item.Evidence.Select(evidence => evidence.Text));
        values.AddRange(item.Segments.Select(segment => segment.EvidenceText));
        values.AddRange(item.Segments.Select(segment => segment.Role));
        values.AddRange(item.Segments.Select(segment => segment.Operation));
        values.AddRange(item.Segments.Select(segment => segment.GeometryType));

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssemblyPrimaryComponentResolution ResolvePrimaryComponent(
        RequirementExtractedItem item)
    {
        var roles = item.Segments
            .Select(segment => ComponentRole(segment.Role ?? segment.Operation))
            .Where(role => role is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0)
        {
            return AssemblyPrimaryComponentResolution.NoOverride();
        }

        var movable = roles
            .Select(role => MovableFunctionalType(role, item))
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (movable.Length == 1)
        {
            var primaryRole = roles.First(role => MovableFunctionalType(role, item) == movable[0]);
            return AssemblyPrimaryComponentResolution.Override(
                movable[0],
                primaryRole,
                OperationFromFunctionalType(movable[0]));
        }

        if (movable.Length > 1)
        {
            return AssemblyPrimaryComponentResolution.RequiresReview(
                [SgTechnicalSelectionReviewReasons
                    .AssemblyMultipleMovableTypesRequiresReview]);
        }

        var specialDomain = PreservedSpecialFunctionalDomain(item);
        if (specialDomain is not null)
        {
            return AssemblyPrimaryComponentResolution.Override(
                specialDomain,
                null,
                item.Operation);
        }

        if (roles.Contains("FIXED", StringComparer.Ordinal))
        {
            return AssemblyPrimaryComponentResolution.Override("FIXED", "FIXED", "FIXED");
        }

        if (roles.Any(role => role is "GRILLE" or "LOUVER"))
        {
            return AssemblyPrimaryComponentResolution.Override("GRILLE", "GRILLE", null);
        }

        return AssemblyPrimaryComponentResolution.RequiresReview(
            [SgTechnicalSelectionReviewReasons
                .TechnicalSelectionCatalogMetadataIncomplete]);
    }

    private static string? PreservedFunctionalType(
        RequirementExtractedItem item,
        string primaryRole)
    {
        var functionalType = NormalizedFunctionalType(item.FunctionalType);
        if (functionalType is null)
        {
            return null;
        }

        if (IsSpecialFunctionalDomain(functionalType))
        {
            return functionalType;
        }

        return primaryRole switch
        {
            "SLIDING" when functionalType is "SLIDING_WINDOW" or "SLIDING_DOOR" =>
                functionalType,
            "PROJECTING" when functionalType == "PROJECTING" => functionalType,
            "SWING" when functionalType == "SWING_DOOR" => functionalType,
            "CASEMENT" when functionalType == "CASEMENT" => functionalType,
            "FOLDING" when functionalType is "FOLDING_WINDOW" or "FOLDING_DOOR" =>
                functionalType,
            "FIXED" when functionalType == "FIXED" => functionalType,
            "GRILLE" when functionalType is "GRILLE" or "LOUVER" => functionalType,
            _ => null
        };
    }

    private static bool IsSpecialFunctionalDomain(string functionalType) =>
        functionalType is "SHOWER_DIVISION"
            or "SKYLIGHT"
            or "RAILING"
            or "FACADE";

    private static string? PreservedSpecialFunctionalDomain(
        RequirementExtractedItem item)
    {
        var functionalType = NormalizedFunctionalType(item.FunctionalType);
        return functionalType is not null && IsSpecialFunctionalDomain(functionalType)
            ? functionalType
            : null;
    }

    private static string? ComponentRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized switch
        {
            "SLIDING" or "SLIDING_DOOR" or "SLIDING_WINDOW" => "SLIDING",
            "PROJECTING" => "PROJECTING",
            "SWING" or "SWING_DOOR" => "SWING",
            "CASEMENT" => "CASEMENT",
            "FOLDING" or "FOLDING_DOOR" or "FOLDING_WINDOW" => "FOLDING",
            "FIXED" => "FIXED",
            "GRILLE" or "LOUVER" => "GRILLE",
            _ => null
        };
    }

    private static string? MovableFunctionalType(
        string role,
        RequirementExtractedItem item) =>
        role switch
        {
            "SLIDING" => PreservedFunctionalType(item, role)
                ?? (item.ElementType == StructuredElementType.Window
                ? "SLIDING_WINDOW"
                : "SLIDING_DOOR"),
            "PROJECTING" => PreservedFunctionalType(item, role) ?? "PROJECTING",
            "SWING" => PreservedFunctionalType(item, role) ?? "SWING_DOOR",
            "CASEMENT" => PreservedFunctionalType(item, role) ?? "CASEMENT",
            "FOLDING" => PreservedFunctionalType(item, role)
                ?? (item.ElementType == StructuredElementType.Window
                ? "FOLDING_WINDOW"
                : "FOLDING_DOOR"),
            _ => null
        };

    private static string? OperationFromFunctionalType(string functionalType) =>
        functionalType switch
        {
            "SLIDING_DOOR" or "SLIDING_WINDOW" => "SLIDING",
            "PROJECTING" => "PROJECTING",
            "SWING_DOOR" => "SWING",
            "CASEMENT" => "CASEMENT",
            "FOLDING_DOOR" or "FOLDING_WINDOW" => "FOLDING",
            _ => null
        };

    private static PrimaryComponentGeometryResolution ResolvePrimaryComponentGeometry(
        RequirementExtractedItem item,
        AssemblyPrimaryComponentResolution primary)
    {
        if (!primary.OverridesItem || primary.PrimaryRole is null)
        {
            return PrimaryComponentGeometryResolution.FromElement(
                item.WidthMillimeters,
                item.HeightMillimeters);
        }

        var matchingSegments = item.Segments
            .Where(segment => ComponentRole(segment.Role ?? segment.Operation) == primary.PrimaryRole)
            .OrderBy(segment => segment.Sequence)
            .ToArray();

        var explicitGeometry = matchingSegments
            .Where(segment => segment.WidthMillimeters is > 0
                && segment.HeightMillimeters is > 0)
            .OrderByDescending(segment => segment.HeightMillimeters)
            .ThenByDescending(segment => segment.WidthMillimeters)
            .FirstOrDefault();

        if (explicitGeometry is not null)
        {
            return PrimaryComponentGeometryResolution.FromComponent(
                explicitGeometry.WidthMillimeters,
                explicitGeometry.HeightMillimeters);
        }

        if (item.Segments.Count == 0
            || (item.Segments.Count == 1 && matchingSegments.Length == 1))
        {
            return PrimaryComponentGeometryResolution.FromElement(
                item.WidthMillimeters,
                item.HeightMillimeters);
        }

        return PrimaryComponentGeometryResolution.Unresolved();
    }

    private static bool IsDimensionDependentFunctionalClassification(
        string? functionalType,
        string? operation)
    {
        var normalizedFunctionalType = NormalizedFunctionalType(functionalType);
        var normalizedOperation = NormalizedFunctionalType(operation);

        return normalizedFunctionalType is "WINDOW" or "SLIDING_WINDOW"
            || normalizedOperation is "SLIDING";
    }

    private sealed record SystemSelectionContext(
        SgTechnicalSelectionInput Input,
        AssemblyPrimaryComponentResolution PrimaryComponent,
        PrimaryComponentGeometryResolution Geometry,
        IReadOnlyList<string> ReviewReasons);

    private sealed record AssemblyPrimaryComponentResolution(
        bool OverridesItem,
        string? FunctionalType,
        string? PrimaryRole,
        string? Operation,
        IReadOnlyList<string> ReviewReasons)
    {
        public static AssemblyPrimaryComponentResolution NoOverride() =>
            new(false, null, null, null, []);

        public static AssemblyPrimaryComponentResolution Override(
            string functionalType,
            string? primaryRole,
            string? operation) =>
            new(true, functionalType, primaryRole, operation, []);

        public static AssemblyPrimaryComponentResolution RequiresReview(
            IReadOnlyList<string> reviewReasons) =>
            new(false, null, null, null, reviewReasons);
    }

    private sealed record PrimaryComponentGeometryResolution(
        int? WidthMillimeters,
        int? HeightMillimeters,
        bool IsUnresolved)
    {
        public static PrimaryComponentGeometryResolution FromElement(
            int? widthMillimeters,
            int? heightMillimeters) =>
            new(widthMillimeters, heightMillimeters, false);

        public static PrimaryComponentGeometryResolution FromComponent(
            int? widthMillimeters,
            int? heightMillimeters) =>
            new(widthMillimeters, heightMillimeters, false);

        public static PrimaryComponentGeometryResolution Unresolved() =>
            new(null, null, true);
    }

    private static string? ToSelectorLine(RequirementCommercialLine commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => "CLASSIC",
            RequirementCommercialLine.Essential => "ESSENTIAL",
            RequirementCommercialLine.Bioconfort => "BIOCONFORT",
            RequirementCommercialLine.Signature => "SIGNATURE",
            _ => null
        };

    private static string? NormalizedFunctionalType(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant()
                .Replace('-', '_')
                .Replace(' ', '_');

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
