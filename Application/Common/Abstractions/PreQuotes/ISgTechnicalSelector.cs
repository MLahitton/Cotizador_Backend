using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using System.Globalization;
using System.Text;

namespace Application.Common.Abstractions.PreQuotes;

public interface ISgTechnicalSelector
{
    Task<SgTechnicalSelectionResult> SelectAsync(
        SgTechnicalSelectionInput input,
        CancellationToken cancellationToken);
}

public sealed record SgTechnicalSelectionInput(
    string? FunctionalType,
    string? Operation,
    int? WidthMillimeters,
    int? HeightMillimeters,
    decimal? AreaSquareMeters,
    int? PanelCount,
    int? MovablePanelCount,
    int? FixedPanelCount,
    string? Modulation,
    string? OpeningDirection,
    IReadOnlyList<string> SpecialFeatures,
    string? GeometryType,
    string? RequestedCommercialLine,
    string? RequestedSystemRaw,
    string? Configuration = null,
    IReadOnlyList<SgHistoricalSystemEvidence>? HistoricalSystemEvidence = null);

public sealed record SgHistoricalSystemEvidence(
    string ProductSystemCode,
    decimal BestSimilarity,
    decimal AverageSimilarity,
    int SupportCount,
    IReadOnlyList<SgHistoricalSystemExample> Examples);

public sealed record SgHistoricalSystemExample(
    string CandidateId,
    string QuoteId,
    string? HistoricalReference,
    decimal SimilarityScore,
    IReadOnlyList<string> MatchedFeatures,
    IReadOnlyList<string> Differences,
    string TechnicalExplanation);

public sealed record SgTechnicalSelectionResult(
    string? SuggestedSystemCode,
    string AppliedRuleCode,
    decimal Confidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> Alternatives,
    int HistoricalSupportCount = 0,
    decimal? HistoricalBestSimilarity = null,
    decimal? HistoricalAverageSimilarity = null,
    IReadOnlyList<SgHistoricalSystemExample>? HistoricalExamples = null,
    IReadOnlyList<string>? ResolutionReasons = null);

public static class SgTechnicalSelectionRuleCodes
{
    public const string SystemFixedFermo = "SYSTEM_FIXED_FERMO";
    public const string SystemProjectingSiena = "SYSTEM_PROJECTING_SIENA";
    public const string SystemCasementSiena = "SYSTEM_CASEMENT_SIENA";
    public const string SystemDoubleCasementSiena = "SYSTEM_DOUBLE_CASEMENT_SIENA";
    public const string SystemSwingDoor3890 = "SYSTEM_SWING_DOOR_3890";
    public const string SystemSlidingDoorNapoles = "SYSTEM_SLIDING_DOOR_NAPOLES";
    public const string SystemSlidingDoorPocketNapoles = "SYSTEM_SLIDING_DOOR_POCKET_NAPOLES";
    public const string SystemSpecialPergola = "SYSTEM_SPECIAL_PERGOLA";
    public const string SystemSpecialBathroomDivisionInox = "SYSTEM_SPECIAL_BATHROOM_DIVISION_INOX";
    public const string SystemSpecialLouver = "SYSTEM_SPECIAL_LOUVER";
    public const string SystemSpecialSkylight = "SYSTEM_SPECIAL_SKYLIGHT";
    public const string SystemNoMatchRequiresReview = "SYSTEM_NO_MATCH_REQUIRES_REVIEW";
    public const string SystemSlidingWindowLowLago = "SYSTEM_SLIDING_WINDOW_LOW_LAGO";
    public const string SystemSlidingWindowMonza = "SYSTEM_SLIDING_WINDOW_MONZA";
    public const string SystemCandidateRanking = "SYSTEM_CANDIDATE_RANKING";
    public const string CommercialLineClassicFilter = "COMMERCIAL_LINE_CLASSIC_FILTER";
    public const string CommercialLineSignatureFilter = "COMMERCIAL_LINE_SIGNATURE_FILTER";
    public const string ClassicWindowSlidingLago = "CLASSIC_WINDOW_SLIDING_LAGO";
    public const string ClassicDoorSlidingLucca = "CLASSIC_DOOR_SLIDING_LUCCA";
    public const string VeniceWindowMonza = "VENICE_WINDOW_MONZA";
    public const string VenicePreferOverLsa = "VENICE_PREFER_OVER_LSA";
    public const string WindowHeightOver2600AsDoor = "WINDOW_HEIGHT_OVER_2600_AS_DOOR";
    public const string RuleNotDefinedRequiresReview = "RULE_NOT_DEFINED_REQUIRES_REVIEW";
}

public static class SgTechnicalSelectionReviewReasons
{
    public const string TechnicalSelectionNoMatch = "TECHNICAL_SELECTION_NO_MATCH";
    public const string TechnicalSelectionAmbiguous = "TECHNICAL_SELECTION_AMBIGUOUS";
    public const string TechnicalSelectionCatalogMatchNotFound = "TECHNICAL_SELECTION_CATALOG_MATCH_NOT_FOUND";
    public const string TechnicalSelectionCatalogMetadataIncomplete = "TECHNICAL_SELECTION_CATALOG_METADATA_INCOMPLETE";
    public const string CommercialLineMismatch = "COMMERCIAL_LINE_MISMATCH";
    public const string SlidingWindowThresholdReview = "SLIDING_WINDOW_THRESHOLD_REVIEW";
    public const string BathroomDivisionMaterialUnknown = "BATHROOM_DIVISION_MATERIAL_UNKNOWN";
    public const string SpecialGeometryWithoutConstraints = "SPECIAL_GEOMETRY_WITHOUT_CONSTRAINTS";
}

public sealed class DeterministicSgTechnicalSelector(
    IProductSystemCatalogRepository productSystems,
    ISgProductSystemConstraintEvaluator constraintEvaluator) : ISgTechnicalSelector
{
    private const decimal StrongUniqueConfidence = 0.95m;
    private const decimal StrongWithReviewConfidence = 0.90m;
    private const decimal GoodConfidence = 0.85m;
    private const decimal PossibleConfidence = 0.70m;
    private const decimal UnknownConfidence = 0.40m;
    private const int LowSlidingWindowHeightMillimeters = 1000;
    private const int FunctionalCompatibilityScore = 100;
    private const int UnknownCompatibilityScore = 20;
    private const int ExactVariantMatchScore = 80;
    private const int SpecialFeatureMatchScore = 70;
    private const int StrongHistoricalPriorScore = 60;
    private const int HistoricalSimilarityMaxBonus = 25;
    private const int CommercialLinePreferenceScore = 10;
    private const int ConfirmedCommercialLineScore = 45;
    private const int VeniceOverLsaScore = 15;
    private const int CatalogRequiresReviewPenalty = 20;
    private const int LsaAutomaticPenalty = 15;
    private const int CloseCandidateScoreDelta = 10;
    private const int MaxAlternatives = 3;

    public DeterministicSgTechnicalSelector(
        IProductSystemCatalogRepository productSystems)
        : this(
            productSystems,
            new SgProductSystemConstraintEvaluator(TimeProvider.System))
    {
    }

    public async Task<SgTechnicalSelectionResult> SelectAsync(
        SgTechnicalSelectionInput input,
        CancellationToken cancellationToken)
    {
        var resolutionReasons = FunctionalResolutionReasons(input);
        var systems = await productSystems
            .ListActiveSelectableAsync(cancellationToken);
        if (systems.Count == 0)
        {
            return NoMatch(
                [SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMatchNotFound],
                resolutionReasons);
        }

        if (IsBathroomDivisionWithoutMaterial(input))
        {
            return NoMatch(
                [SgTechnicalSelectionReviewReasons.BathroomDivisionMaterialUnknown],
                resolutionReasons);
        }

        var eligibleSystems = FilterByRequestedCommercialLine(
            systems,
            input,
            out var commercialLineFilterRule);

        var candidates = eligibleSystems
            .Select(system => BuildCandidate(system, input))
            .Where(candidate => candidate.CompatibilityState
                != SgTechnicalCompatibilityState.Incompatible)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Tier)
            .ThenBy(candidate => candidate.ProductSystem.Code,
                StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            return NoMatch(
                [SgTechnicalSelectionReviewReasons.TechnicalSelectionNoMatch],
                resolutionReasons);
        }

        if (candidates.All(candidate => candidate.CompatibilityState
                == SgTechnicalCompatibilityState.Unknown))
        {
            return new(
                null,
                SgTechnicalSelectionRuleCodes.SystemNoMatchRequiresReview,
                0m,
                true,
                [SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete],
                candidates.Take(MaxAlternatives)
                    .Select(candidate => candidate.ProductSystem.Code)
                    .ToArray(),
                ResolutionReasons: resolutionReasons);
        }

        var top = candidates[0];
        var second = candidates.Skip(1).FirstOrDefault();
        if (second is not null && top.Score == second.Score)
        {
            return new(
                null,
                SgTechnicalSelectionRuleCodes.SystemCandidateRanking,
                0m,
                true,
                [SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous],
                candidates.Take(MaxAlternatives)
                    .Select(candidate => candidate.ProductSystem.Code)
                    .ToArray(),
                ResolutionReasons: resolutionReasons);
        }

        var reasons = top.ReviewReasons.ToList();
        if (second is not null
            && top.Score - second.Score <= CloseCandidateScoreDelta)
        {
            reasons.Add(SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous);
        }

        if (HasSpecialGeometry(input))
        {
            reasons.Add(SgTechnicalSelectionReviewReasons.SpecialGeometryWithoutConstraints);
        }

        if (EffectiveFunctionalType(input) == "SLIDING_DOOR"
            && IsNapoles(top.ProductSystem)
            && candidates.Any(candidate => IsMonaco(candidate.ProductSystem)))
        {
            reasons.Add(SgTechnicalSelectionRuleCodes.RuleNotDefinedRequiresReview);
        }

        var confidence = Confidence(top, second, reasons);
        return new(
            top.ProductSystem.Code,
            top.PrimaryRuleCode
                ?? commercialLineFilterRule
                ?? SgTechnicalSelectionRuleCodes.SystemCandidateRanking,
            confidence,
            reasons.Count > 0,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            candidates
                .Where(candidate => candidate.ProductSystem.Code
                    != top.ProductSystem.Code)
                .Take(MaxAlternatives)
                .Select(candidate => candidate.ProductSystem.Code)
                .ToArray(),
            top.HistoricalEvidence?.SupportCount ?? 0,
            top.HistoricalEvidence?.BestSimilarity,
            top.HistoricalEvidence?.AverageSimilarity,
            top.HistoricalEvidence?.Examples ?? [],
            resolutionReasons);
    }

    private SgTechnicalCandidate BuildCandidate(
        ProductSystemCatalogReadModel system,
        SgTechnicalSelectionInput input)
    {
        var candidate = new SgTechnicalCandidate(system);
        ApplyAutomaticCatalogEligibility(candidate);
        if (candidate.CompatibilityState
            == SgTechnicalCompatibilityState.Incompatible)
        {
            return candidate;
        }

        ApplyFunctionalCompatibility(candidate, input);
        if (candidate.CompatibilityState
            == SgTechnicalCompatibilityState.Incompatible)
        {
            return candidate;
        }

        ApplyConfirmedFamilyCompatibility(candidate, input);
        if (candidate.CompatibilityState
            == SgTechnicalCompatibilityState.Incompatible)
        {
            return candidate;
        }

        ApplyPreSelectionConstraints(candidate, input);
        if (candidate.CompatibilityState
            == SgTechnicalCompatibilityState.Incompatible)
        {
            return candidate;
        }

        ApplySpecialFeatures(candidate, input);
        if (candidate.CompatibilityState
            == SgTechnicalCompatibilityState.Incompatible)
        {
            return candidate;
        }

        ApplyPreferencePriors(candidate, input);
        ApplyVeniceOverLsa(candidate, input);
        ApplyHistoricalSimilarity(candidate, input);
        ApplyCommercialLine(candidate, input);
        if (system.RequiresReview)
        {
            candidate.Score -= CatalogRequiresReviewPenalty;
            candidate.ReviewReasons.Add(
                SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete);
        }

        candidate.Tier = Tier(candidate);
        return candidate;
    }

    private static IReadOnlyList<ProductSystemCatalogReadModel>
        FilterByRequestedCommercialLine(
            IReadOnlyList<ProductSystemCatalogReadModel> systems,
            SgTechnicalSelectionInput input,
            out string? ruleCode)
    {
        ruleCode = null;
        var requested = Code(input.RequestedCommercialLine);
        if (requested is null or "ESSENTIAL" or "BIOCONFORT")
        {
            return systems;
        }

        if (requested == "CLASSIC")
        {
            ruleCode = SgTechnicalSelectionRuleCodes.CommercialLineClassicFilter;
            return systems
                .Where(system => Code(system.CommercialLine) == "CLASSIC")
                .ToArray();
        }

        if (requested == "SIGNATURE")
        {
            ruleCode = SgTechnicalSelectionRuleCodes.CommercialLineSignatureFilter;
            return systems
                .Where(system => Code(system.CommercialLine) == "SIGNATURE")
                .ToArray();
        }

        return systems;
    }

    private static void ApplyAutomaticCatalogEligibility(
        SgTechnicalCandidate candidate)
    {
        if (Code(candidate.ProductSystem.CommercialLine) == "TRADITIONAL")
        {
            candidate.CompatibilityState =
                SgTechnicalCompatibilityState.Incompatible;
            candidate.FailedRuleCodes.Add("TRADITIONAL_SYSTEM_NOT_AUTOMATIC");
        }
    }

    private static void ApplyConfirmedFamilyCompatibility(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        if (EffectiveFunctionalType(input) == "SLIDING_DOOR"
            && IsMonza(candidate.ProductSystem))
        {
            candidate.CompatibilityState =
                SgTechnicalCompatibilityState.Incompatible;
            candidate.FailedRuleCodes.Add("VENICE_MONZA_WINDOW_ONLY");
        }
    }

    private static void ApplyFunctionalCompatibility(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var expected = EffectiveFunctionalType(input);
        var actual = Code(candidate.ProductSystem.FunctionalType);
        if (expected is null || actual is null)
        {
            candidate.CompatibilityState = SgTechnicalCompatibilityState.Unknown;
            candidate.Score += UnknownCompatibilityScore;
            candidate.ReviewReasons.Add(
                SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete);
            return;
        }

        if (expected != actual)
        {
            candidate.CompatibilityState = SgTechnicalCompatibilityState.Incompatible;
            candidate.FailedRuleCodes.Add("FUNCTIONAL_TYPE_COMPATIBILITY");
            return;
        }

        candidate.CompatibilityState = SgTechnicalCompatibilityState.Compatible;
        candidate.Score += FunctionalCompatibilityScore;
        candidate.MatchedRuleCodes.Add("FUNCTIONAL_TYPE_COMPATIBILITY");
    }

    private void ApplyPreSelectionConstraints(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var result = constraintEvaluator.Evaluate(
            candidate.ProductSystem,
            input,
            ConstraintEvaluationStage.PreSelection);
        if (result.HasHardFailure)
        {
            candidate.CompatibilityState =
                SgTechnicalCompatibilityState.Incompatible;
            candidate.FailedRuleCodes.AddRange(result.Evaluations
                .Where(value => value.State
                    == ProductSystemConstraintEvaluationState.Fail)
                .Select(value => value.ConstraintCode));
            return;
        }

        candidate.ReviewReasons.AddRange(result.ReviewReasons);
    }

    private static void ApplySpecialFeatures(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var features = Features(input);
        if (EffectiveFunctionalType(input) == "SLIDING_DOOR"
            && features.Contains("POCKET"))
        {
            if (Matches(candidate.ProductSystem.Variant, "POCKET"))
            {
                candidate.Score += ExactVariantMatchScore
                    + SpecialFeatureMatchScore;
                candidate.MatchedRuleCodes.Add("SPECIAL_FEATURE_POCKET");
                return;
            }

            if (Code(candidate.ProductSystem.Variant) is not null)
            {
                candidate.CompatibilityState =
                    SgTechnicalCompatibilityState.Incompatible;
                candidate.FailedRuleCodes.Add("SPECIAL_FEATURE_POCKET");
            }
        }

        if (EffectiveFunctionalType(input) == "SHOWER_DIVISION"
            && HasInox(input, features)
            && IsInox(candidate.ProductSystem))
        {
            candidate.Score += ExactVariantMatchScore
                + SpecialFeatureMatchScore;
            candidate.MatchedRuleCodes.Add("SPECIAL_FEATURE_INOX");
        }
    }

    private static void ApplyPreferencePriors(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var functionalType = EffectiveFunctionalType(input);
        var operation = Code(input.Operation);
        var features = Features(input);
        var prior = PriorFor(candidate.ProductSystem, functionalType,
            operation, features, input.HeightMillimeters,
            input.RequestedCommercialLine);
        if (prior is null)
        {
            return;
        }

        candidate.Score += StrongHistoricalPriorScore;
        candidate.PrimaryRuleCode ??= prior;
        candidate.MatchedRuleCodes.Add(prior);
        if (prior is SgTechnicalSelectionRuleCodes.SystemSlidingWindowLowLago
            or SgTechnicalSelectionRuleCodes.SystemSlidingWindowMonza
            or SgTechnicalSelectionRuleCodes.VeniceWindowMonza)
        {
            candidate.ReviewReasons.Add(
                SgTechnicalSelectionReviewReasons.SlidingWindowThresholdReview);
        }
    }

    private static string? PriorFor(
        ProductSystemCatalogReadModel system,
        string? functionalType,
        string? operation,
        IReadOnlySet<string> features,
        int? heightMillimeters,
        string? requestedCommercialLine)
    {
        if (Code(requestedCommercialLine) == "CLASSIC"
            && Code(system.CommercialLine) == "CLASSIC"
            && functionalType == "SLIDING_WINDOW"
            && Matches(system.Family, "PRIMAVERA LAGO"))
        {
            return SgTechnicalSelectionRuleCodes.ClassicWindowSlidingLago;
        }

        if (Code(requestedCommercialLine) == "CLASSIC"
            && Code(system.CommercialLine) == "CLASSIC"
            && functionalType == "SLIDING_DOOR"
            && Matches(system.Family, "PRIMAVERA LUCCA"))
        {
            return SgTechnicalSelectionRuleCodes.ClassicDoorSlidingLucca;
        }

        if (functionalType == "FIXED" && Matches(system.Family, "VENECIA FERMO"))
        {
            return SgTechnicalSelectionRuleCodes.SystemFixedFermo;
        }

        if (functionalType is "PROJECTING" or "CASEMENT" or "DOUBLE_CASEMENT"
            && Matches(system.Family, "PRIMAVERA SIENA"))
        {
            return functionalType switch
            {
                "CASEMENT" => SgTechnicalSelectionRuleCodes.SystemCasementSiena,
                "DOUBLE_CASEMENT" => SgTechnicalSelectionRuleCodes.SystemDoubleCasementSiena,
                _ => SgTechnicalSelectionRuleCodes.SystemProjectingSiena
            };
        }

        if ((functionalType == "SWING_DOOR"
                || functionalType == "DOOR" && operation == "SWING")
            && Matches(system.Family, "SG 3890"))
        {
            return SgTechnicalSelectionRuleCodes.SystemSwingDoor3890;
        }

        if (functionalType == "SLIDING_DOOR"
            && Matches(system.Family, "VENECIA NAPOLES"))
        {
            if (features.Contains("POCKET") && Matches(system.Variant, "POCKET"))
            {
                return SgTechnicalSelectionRuleCodes.SystemSlidingDoorPocketNapoles;
            }

            if (!features.Contains("POCKET")
                && (Code(system.Variant) is null || Matches(system.Variant, "STANDARD")))
            {
                return SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles;
            }
        }

        if (functionalType == "SLIDING_WINDOW"
            && heightMillimeters is <= LowSlidingWindowHeightMillimeters
            && Matches(system.Family, "PRIMAVERA LAGO"))
        {
            return SgTechnicalSelectionRuleCodes.SystemSlidingWindowLowLago;
        }

        if (functionalType == "SLIDING_WINDOW"
            && heightMillimeters is null or > LowSlidingWindowHeightMillimeters
            && Matches(system.Family, "VENECIA MONZA"))
        {
            return SgTechnicalSelectionRuleCodes.VeniceWindowMonza;
        }

        if (functionalType == "PERGOLA")
        {
            return SgTechnicalSelectionRuleCodes.SystemSpecialPergola;
        }

        if (functionalType == "SHOWER_DIVISION" && IsInox(system))
        {
            return SgTechnicalSelectionRuleCodes.SystemSpecialBathroomDivisionInox;
        }

        if (functionalType == "GRILLE")
        {
            return SgTechnicalSelectionRuleCodes.SystemSpecialLouver;
        }

        if (functionalType == "SKYLIGHT")
        {
            return SgTechnicalSelectionRuleCodes.SystemSpecialSkylight;
        }

        return null;
    }

    private static void ApplyCommercialLine(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var requested = Code(input.RequestedCommercialLine);
        if (requested is null)
        {
            return;
        }

        var actual = Code(candidate.ProductSystem.CommercialLine);
        if (actual is null)
        {
            candidate.ReviewReasons.Add(
                SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete);
            return;
        }

        if (requested == actual)
        {
            candidate.Score += CommercialLinePreferenceScore;
            candidate.MatchedRuleCodes.Add("COMMERCIAL_LINE_PREFERENCE");
            return;
        }

        if (requested is "ESSENTIAL" or "BIOCONFORT")
        {
            return;
        }

        candidate.ReviewReasons.Add(
            SgTechnicalSelectionReviewReasons.CommercialLineMismatch);
    }

    private static void ApplyVeniceOverLsa(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        var functionalType = EffectiveFunctionalType(input);
        if (functionalType is not ("SLIDING_DOOR" or "SLIDING_WINDOW"))
        {
            return;
        }

        if (IsLsa(candidate.ProductSystem))
        {
            candidate.Score -= LsaAutomaticPenalty;
            return;
        }

        if (IsVenice(candidate.ProductSystem))
        {
            candidate.Score += VeniceOverLsaScore;
            candidate.MatchedRuleCodes.Add(
                SgTechnicalSelectionRuleCodes.VenicePreferOverLsa);
        }
    }

    private static void ApplyHistoricalSimilarity(
        SgTechnicalCandidate candidate,
        SgTechnicalSelectionInput input)
    {
        if (input.HistoricalSystemEvidence is not { Count: > 0 } evidence)
        {
            return;
        }

        var candidateCode = Code(candidate.ProductSystem.Code);
        if (candidateCode is null)
        {
            return;
        }

        var match = evidence.FirstOrDefault(value =>
            Code(value.ProductSystemCode) == candidateCode);
        if (match is null || match.SupportCount <= 0)
        {
            return;
        }

        var best = ClampSimilarity(match.BestSimilarity);
        var average = ClampSimilarity(match.AverageSimilarity);
        var support = Math.Min(match.SupportCount, 5);
        var bonus = (int)Math.Round(
            best * 15m + average * 7m + support * 0.6m,
            MidpointRounding.AwayFromZero);

        candidate.Score += Math.Min(HistoricalSimilarityMaxBonus, bonus);
        candidate.HistoricalEvidence = match;
        candidate.MatchedRuleCodes.Add("HISTORICAL_SIMILARITY");
    }

    private static decimal Confidence(
        SgTechnicalCandidate top,
        SgTechnicalCandidate? second,
        IReadOnlyCollection<string> reviewReasons)
    {
        if (top.CompatibilityState == SgTechnicalCompatibilityState.Unknown)
        {
            return UnknownConfidence;
        }

        var confidence = top.Tier switch
        {
            SgTechnicalCandidateTier.StrongMatch => StrongUniqueConfidence,
            SgTechnicalCandidateTier.GoodMatch => GoodConfidence,
            SgTechnicalCandidateTier.Possible => PossibleConfidence,
            _ => UnknownConfidence
        };

        if (second is not null && top.Score - second.Score <= CloseCandidateScoreDelta)
        {
            confidence = Math.Min(confidence, PossibleConfidence);
        }

        if (reviewReasons.Count > 0)
        {
            confidence = Math.Min(confidence, StrongWithReviewConfidence);
        }

        if (reviewReasons.Contains(
                SgTechnicalSelectionReviewReasons.SlidingWindowThresholdReview))
        {
            confidence = Math.Min(confidence, PossibleConfidence);
        }

        return confidence;
    }

    private static SgTechnicalCandidateTier Tier(SgTechnicalCandidate candidate)
    {
        if (candidate.CompatibilityState == SgTechnicalCompatibilityState.Unknown)
        {
            return SgTechnicalCandidateTier.Unknown;
        }

        return candidate.Score switch
        {
            >= FunctionalCompatibilityScore + ExactVariantMatchScore
                + StrongHistoricalPriorScore => SgTechnicalCandidateTier.StrongMatch,
            >= FunctionalCompatibilityScore + StrongHistoricalPriorScore
                => SgTechnicalCandidateTier.StrongMatch,
            >= FunctionalCompatibilityScore => SgTechnicalCandidateTier.GoodMatch,
            _ => SgTechnicalCandidateTier.Possible
        };
    }

    private static bool IsBathroomDivisionWithoutMaterial(
        SgTechnicalSelectionInput input) =>
        EffectiveFunctionalType(input) == "SHOWER_DIVISION"
        && !HasInox(input, Features(input));

    private static bool HasSpecialGeometry(SgTechnicalSelectionInput input) =>
        Code(input.GeometryType) is "L_SHAPE" or "CORNER" or "TRIANGULAR" or "ARCH" or "CURVED";

    private static bool HasInox(
        SgTechnicalSelectionInput input,
        IReadOnlySet<string> features) =>
        features.Contains("INOX")
        || Contains(input.Configuration, "INOX")
        || Contains(input.RequestedSystemRaw, "INOX");

    private static bool IsInox(ProductSystemCatalogReadModel system) =>
        Matches(system.Variant, "INOX")
        || Contains(system.Family, "INOX")
        || Contains(system.TechnicalName, "INOX")
        || Contains(system.CommercialName, "INOX");

    private static bool IsVenice(ProductSystemCatalogReadModel system) =>
        Contains(system.Family, "VENECIA")
        || Contains(system.TechnicalName, "VENECIA")
        || Contains(system.CommercialName, "VENECIA")
        || Contains(system.Name, "VENECIA");

    private static bool IsMonza(ProductSystemCatalogReadModel system) =>
        Contains(system.Family, "MONZA")
        || Contains(system.TechnicalName, "MONZA")
        || Contains(system.CommercialName, "MONZA")
        || Contains(system.Name, "MONZA");

    private static bool IsNapoles(ProductSystemCatalogReadModel system) =>
        Contains(system.Family, "NAPOLES")
        || Contains(system.TechnicalName, "NAPOLES")
        || Contains(system.CommercialName, "NAPOLES")
        || Contains(system.Name, "NAPOLES");

    private static bool IsMonaco(ProductSystemCatalogReadModel system) =>
        Contains(system.Family, "MONACO")
        || Contains(system.Family, "MÓNACO")
        || Contains(system.TechnicalName, "MONACO")
        || Contains(system.TechnicalName, "MÓNACO")
        || Contains(system.CommercialName, "MONACO")
        || Contains(system.CommercialName, "MÓNACO")
        || Contains(system.Name, "MONACO")
        || Contains(system.Name, "MÓNACO");

    private static bool IsLsa(ProductSystemCatalogReadModel system) =>
        Contains(system.Family, "LSA")
        || Contains(system.TechnicalName, "LSA")
        || Contains(system.CommercialName, "LSA")
        || Contains(system.Name, "LSA")
        || system.Code.StartsWith("LSA", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Features(SgTechnicalSelectionInput input) =>
        (input.SpecialFeatures ?? [])
        .Select(Code)
        .Where(value => value is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static bool Matches(string? actual, string? expected) =>
        expected is null || Code(actual) == Code(expected);

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? EffectiveFunctionalType(
        SgTechnicalSelectionInput input)
    {
        var functionalType = Code(input.FunctionalType);
        var operation = Code(input.Operation);
        if (functionalType == "WINDOW"
            && input.HeightMillimeters > 2600)
        {
            return operation switch
            {
                "SLIDING" => "SLIDING_DOOR",
                "SWING" => "SWING_DOOR",
                _ => "DOOR"
            };
        }

        if (functionalType == "SLIDING_WINDOW"
            && input.HeightMillimeters > 2600)
        {
            return "SLIDING_DOOR";
        }

        if (functionalType == "WINDOW")
        {
            return operation switch
            {
                "FIXED" => "FIXED",
                "SLIDING" => "SLIDING_WINDOW",
                _ => functionalType
            };
        }

        return functionalType;
    }

    private static IReadOnlyList<string> FunctionalResolutionReasons(
        SgTechnicalSelectionInput input) =>
        Code(input.FunctionalType) is "WINDOW" or "SLIDING_WINDOW"
        && input.HeightMillimeters > 2600
            ? [SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor]
            : [];

    internal static string NormalizeTechnicalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            if (char.IsLetterOrDigit(upper))
            {
                builder.Append(upper);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string? Code(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeTechnicalText(value);
        return normalized switch
        {
            "BATHROOM DIVISION" or "BATHROOM_DIVISION" => "SHOWER_DIVISION",
            "LOUVER" => "GRILLE",
            "VENTANA CORREDERA" or "CORREDERA 02 HOJAS" => "SLIDING_WINDOW",
            "VENTANA FIJA" or "FIJA SIMPLE" => "FIXED",
            "VENTANA DE ESQUINA" => "WINDOW",
            "CORREDERA" => "SLIDING",
            "FIJA" => "FIXED",
            _ => value.Trim().ToUpperInvariant()
        };
    }

    private static decimal ClampSimilarity(decimal value) =>
        Math.Max(0m, Math.Min(1m, value));

    private static SgTechnicalSelectionResult NoMatch(
        IReadOnlyList<string> reasons,
        IReadOnlyList<string>? resolutionReasons = null) =>
        new(
            null,
            SgTechnicalSelectionRuleCodes.SystemNoMatchRequiresReview,
            0m,
            true,
            reasons,
            [],
            ResolutionReasons: resolutionReasons);

    private sealed class SgTechnicalCandidate(
        ProductSystemCatalogReadModel productSystem)
    {
        public ProductSystemCatalogReadModel ProductSystem { get; } =
            productSystem;
        public SgTechnicalCompatibilityState CompatibilityState { get; set; } =
            SgTechnicalCompatibilityState.Unknown;
        public int Score { get; set; }
        public SgTechnicalCandidateTier Tier { get; set; } =
            SgTechnicalCandidateTier.Unknown;
        public string? PrimaryRuleCode { get; set; }
        public SgHistoricalSystemEvidence? HistoricalEvidence { get; set; }
        public List<string> MatchedRuleCodes { get; } = [];
        public List<string> FailedRuleCodes { get; } = [];
        public List<string> ReviewReasons { get; } = [];
    }

    private enum SgTechnicalCompatibilityState
    {
        Compatible = 1,
        Incompatible,
        Unknown
    }

    private enum SgTechnicalCandidateTier
    {
        StrongMatch = 1,
        GoodMatch,
        Possible,
        Unknown
    }
}
