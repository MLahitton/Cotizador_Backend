using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementExperience;

public sealed class RequirementExperienceLevel2Resolver(
    ISgProductSystemConstraintEvaluator constraintEvaluator,
    IGlassCandidateResolver glassCandidateResolver,
    ISgTechnicalSelector technicalSelector)
{
    public const string PreferencesIncomplete = "EXPERIENCE_PREFERENCES_INCOMPLETE";

    public async Task<RequirementExperienceLevel2Response> ResolveAsync(
        RequirementTechnicalProposal technicalProposal,
        RequirementTechnicalProposalItem item,
        RequirementExperienceLevel1ResolutionResponse? level1Resolution,
        RequirementItemExperienceTechnicalSelection? technicalSelection,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        CancellationToken cancellationToken)
    {
        var systemInput = RequirementFunctionalSelectionContextResolver.ResolveInput(
            item,
            requestedCommercialLine: null) with
        {
            RequestedCommercialLine = null
        };
        var effectiveFunctionalType = SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(systemInput);
        var finish = new RequirementExperienceLevel2FinishResponse(
            RequirementExperienceLevel2States.PendingExternalSource, [], []);
        if (level1Resolution is not { IsComplete: true }
            || level1Resolution.SystemTier is not ("CLASSIC" or "PREMIUM")
            || level1Resolution.GlassFamily is not ("TEMPLADO" or "LAMINADO"))
        {
            return new RequirementExperienceLevel2Response(
                technicalProposal.Id, item.Id, technicalSelection?.Revision, effectiveFunctionalType,
                new(RequirementExperienceLevel2States.Pending, null,
                    technicalSelection?.SystemId is { } systemId ? MapStaleSystemOverride(systemId, systems) : null,
                    null, RequirementExperienceLevel2OverrideStates.None, [], [], [PreferencesIncomplete]),
                new(RequirementExperienceLevel2States.Pending, null, null,
                    technicalSelection?.GlassTypeId is { } glassId ? MapStaleGlassOverride(glassId, glasses) : null,
                    null, RequirementExperienceLevel2OverrideStates.None, [], [], [PreferencesIncomplete]),
                finish, RequirementExperienceLevel2States.Pending);
        }

        var tierSystems = systems.Where(system => system.IsActive && system.IsSelectable
            && ProductSystemExperienceTierResolver.Resolve(system) == level1Resolution.SystemTier).ToArray();
        var system = await ResolveSystemAsync(systemInput, tierSystems, cancellationToken,
            new DeterministicSgTechnicalSelector(new ExperienceSystemCatalog(tierSystems), constraintEvaluator));
        var glass = ResolveGlass(
            item.ExtractedItem,
            level1Resolution?.GlassFamily,
            glasses,
            new RequirementGlassDimensions(
                item.EffectiveWidthMillimeters,
                item.EffectiveHeightMillimeters));

        return new RequirementExperienceLevel2Response(
            technicalProposal.Id,
            item.Id,
            technicalSelection?.Revision,
            effectiveFunctionalType,
            ApplySystemOverride(system, technicalSelection?.SystemId, systems),
            ApplyGlassOverride(glass, technicalSelection?.GlassTypeId, glasses),
            finish,
            OverallState(system.State, glass.State));
    }

    public async Task<RequirementExperienceLevel2SystemResponse> ResolveSystemAsync(
        SgTechnicalSelectionInput input,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        CancellationToken cancellationToken = default,
        ISgTechnicalSelector? candidateSelector = null)
    {
        var neutralInput = input with
        {
            RequestedCommercialLine = null,
            EnforceLegacyCommercialLineEligibility = false
        };
        var effectiveFunctionalType = SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(neutralInput);
        if (effectiveFunctionalType is null)
        {
            return new RequirementExperienceLevel2SystemResponse(
                RequirementExperienceLevel2States.RequiresReview,
                Suggested: null,
                Override: null,
                Effective: null,
                RequirementExperienceLevel2OverrideStates.None,
                Candidates: [],
                ReviewReasons: [SgFunctionalCompatibilityEvaluator.TechnicalProposalFunctionalTypeUnknown],
                BlockingReasons: []);
        }

        var eligibilityEvaluator = new DeterministicSgTechnicalSelector(
            new ExperienceSystemCatalog(systems), constraintEvaluator);
        var candidates = systems
            .Where(system => system.IsActive && system.IsSelectable)
            .Select(system => MapSystemCandidate(neutralInput, system, eligibilityEvaluator))
            .Where(candidate => candidate is not null)
            .Cast<RequirementExperienceLevel2SystemCandidateResponse>()
            .ToArray();

        var compatible = candidates
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.Compatible)
            .ToArray();
        var review = candidates
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.RequiresReview)
            .ToArray();

        var selection = compatible.Length == 0
            ? null
            : await (candidateSelector ?? technicalSelector).SelectAsync(neutralInput, cancellationToken);
        var suggestedCode = selection?.SuggestedSystemCode;
        var suggested = suggestedCode is null
            ? null
            : compatible.FirstOrDefault(candidate => SameCode(candidate.Code, suggestedCode));

        var ordered = OrderSystemCandidates(candidates, suggested?.Code);
        compatible = ordered
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.Compatible)
            .ToArray();
        review = ordered
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.RequiresReview)
            .ToArray();

        if (suggested is not null)
        {
            return new RequirementExperienceLevel2SystemResponse(
                RequirementExperienceLevel2States.Resolved,
                suggested,
                Override: null,
                Effective: suggested,
                RequirementExperienceLevel2OverrideStates.None,
                ordered,
                ReviewReasons: [],
                BlockingReasons: []);
        }

        if (review.Length > 0 || compatible.Length > 0)
        {
            return new RequirementExperienceLevel2SystemResponse(
                RequirementExperienceLevel2States.RequiresReview,
                Suggested: null,
                Override: null,
                Effective: null,
                RequirementExperienceLevel2OverrideStates.None,
                Candidates: ordered,
                ReviewReasons: review
                    .SelectMany(candidate => candidate.ReviewReasons)
                    .Concat(selection?.ReviewReasons ?? [])
                    .DefaultIfEmpty(SgTechnicalSelectionReviewReasons.TechnicalSelectionNoMatch)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                BlockingReasons: []);
        }

        return new RequirementExperienceLevel2SystemResponse(
            RequirementExperienceLevel2States.Blocked,
            Suggested: null,
            Override: null,
            Effective: null,
            RequirementExperienceLevel2OverrideStates.None,
            Candidates: ordered,
            ReviewReasons: [],
            BlockingReasons: ordered
                .SelectMany(candidate => candidate.BlockingReasons)
                .DefaultIfEmpty("SYSTEM_NO_COMPATIBLE_CANDIDATE")
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private sealed class ExperienceSystemCatalog(IReadOnlyList<ProductSystemCatalogReadModel> systems)
        : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveAsync(CancellationToken cancellationToken)
            => Task.FromResult(systems);
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveSelectableAsync(CancellationToken cancellationToken)
            => Task.FromResult(systems);
        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(systems.FirstOrDefault(system => SameCode(system.Code, code)));
    }

    public RequirementExperienceLevel2GlassResponse ResolveGlass(
        RequirementExtractedItem? item,
        string? glassFamily,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        RequirementGlassDimensions? dimensions = null)
    {
        var normalizedFamily = Normalize(glassFamily);
        if (normalizedFamily is null)
        {
            return new RequirementExperienceLevel2GlassResponse(
                RequirementExperienceLevel2States.Pending,
                glassFamily,
                Suggested: null,
                Override: null,
                Effective: null,
                RequirementExperienceLevel2OverrideStates.None,
                Candidates: [],
                ReviewReasons: ["GLASS_FAMILY_NOT_RESOLVED"],
                BlockingReasons: []);
        }

        var filteredGlasses = glasses
            .Where(glass => glass.IsActive && glass.IsSelectable)
            .Where(glass => MatchesLevel1GlassFamily(glass, normalizedFamily))
            .ToArray();
        if (filteredGlasses.Length == 0)
        {
            return new RequirementExperienceLevel2GlassResponse(
                RequirementExperienceLevel2States.Blocked,
                glassFamily,
                Suggested: null,
                Override: null,
                Effective: null,
                RequirementExperienceLevel2OverrideStates.None,
                Candidates: [],
                ReviewReasons: [],
                BlockingReasons: ["GLASS_NO_COMPATIBLE_CANDIDATE"]);
        }

        var technicalResolution = item is null
            ? null
            : RequirementGlassTechnicalResolver.ResolveByFamily(
                item,
                normalizedFamily,
                filteredGlasses,
                dimensions);
        var signalResolution = technicalResolution is null && item is not null
            ? glassCandidateResolver.Resolve(item, filteredGlasses)
            : null;
        var resolution = technicalResolution ?? signalResolution;
        var confirmedSuggestedId = resolution?.RequiresReview == false
            ? resolution.Suggested?.GlassTypeId
            : null;

        var candidates = filteredGlasses
            .Select(glass => MapGlassCandidate(
                glass,
                confirmedSuggestedId is not null,
                resolution?.ReviewReasons ?? []))
            .ToArray();
        var suggested = confirmedSuggestedId is { } id
            ? candidates.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        var ordered = OrderGlassCandidates(candidates, suggested?.Code);
        var compatible = ordered
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.Compatible)
            .ToArray();
        var review = ordered
            .Where(candidate => candidate.CompatibilityState == RequirementExperienceLevel2CandidateStates.RequiresReview)
            .ToArray();

        if (compatible.Length > 0)
        {
            suggested ??= compatible.First();
            return new RequirementExperienceLevel2GlassResponse(
                RequirementExperienceLevel2States.Resolved,
                glassFamily,
                suggested,
                Override: null,
                Effective: suggested,
                RequirementExperienceLevel2OverrideStates.None,
                ordered,
                ReviewReasons: [],
                BlockingReasons: []);
        }

        if (review.Length > 0)
        {
            return new RequirementExperienceLevel2GlassResponse(
                RequirementExperienceLevel2States.RequiresReview,
                glassFamily,
                Suggested: null,
                Override: null,
                Effective: null,
                RequirementExperienceLevel2OverrideStates.None,
                Candidates: ordered,
                ReviewReasons: review
                    .SelectMany(candidate => candidate.ReviewReasons)
                    .DefaultIfEmpty("GLASS_TECHNICAL_RESOLUTION_REQUIRES_REVIEW")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                BlockingReasons: []);
        }

        return new RequirementExperienceLevel2GlassResponse(
            RequirementExperienceLevel2States.Blocked,
            glassFamily,
            Suggested: null,
            Override: null,
            Effective: null,
            RequirementExperienceLevel2OverrideStates.None,
            Candidates: ordered,
            ReviewReasons: [],
            BlockingReasons: ["GLASS_NO_COMPATIBLE_CANDIDATE"]);
    }

    public static RequirementExperienceLevel2SystemResponse ApplySystemOverride(
        RequirementExperienceLevel2SystemResponse system,
        Guid? systemId,
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
    {
        if (systemId is null)
        {
            return system with
            {
                Override = null,
                Effective = system.Suggested,
                OverrideState = RequirementExperienceLevel2OverrideStates.None
            };
        }

        var candidate = system.Candidates.FirstOrDefault(value => value.Id == systemId);
        if (candidate is null
            || candidate.CompatibilityState
                == RequirementExperienceLevel2CandidateStates.Incompatible)
        {
            return system with
            {
                Override = MapStaleSystemOverride(systemId.Value, systems),
                Effective = system.Suggested,
                OverrideState = RequirementExperienceLevel2OverrideStates.Stale
            };
        }

        var state = candidate.CompatibilityState
            == RequirementExperienceLevel2CandidateStates.RequiresReview
                ? RequirementExperienceLevel2OverrideStates.RequiresReview
                : RequirementExperienceLevel2OverrideStates.Valid;

        return system with
        {
            Override = candidate,
            Effective = candidate,
            OverrideState = state
        };
    }

    public static RequirementExperienceLevel2GlassResponse ApplyGlassOverride(
        RequirementExperienceLevel2GlassResponse glass,
        Guid? glassTypeId,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        if (glassTypeId is null)
        {
            return glass with
            {
                Override = null,
                Effective = glass.Suggested,
                OverrideState = RequirementExperienceLevel2OverrideStates.None
            };
        }

        var candidate = glass.Candidates.FirstOrDefault(value => value.Id == glassTypeId);
        if (candidate is null
            || candidate.CompatibilityState
                == RequirementExperienceLevel2CandidateStates.Incompatible)
        {
            return glass with
            {
                Override = MapStaleGlassOverride(glassTypeId.Value, glasses),
                Effective = glass.Suggested,
                OverrideState = RequirementExperienceLevel2OverrideStates.Stale
            };
        }

        var state = candidate.CompatibilityState
            == RequirementExperienceLevel2CandidateStates.RequiresReview
                ? RequirementExperienceLevel2OverrideStates.RequiresReview
                : RequirementExperienceLevel2OverrideStates.Valid;

        return glass with
        {
            Override = candidate,
            Effective = candidate,
            OverrideState = state
        };
    }

    private static RequirementExperienceLevel2SystemCandidateResponse? MapSystemCandidate(
        SgTechnicalSelectionInput input,
        ProductSystemCatalogReadModel system,
        DeterministicSgTechnicalSelector eligibilityEvaluator)
    {
        if (SgFunctionalCompatibilityEvaluator.Code(system.FunctionalType) is null)
        {
            return null;
        }

        var functional = SgFunctionalCompatibilityEvaluator.Evaluate(input, system);
        if (!functional.IsCompatible)
        {
            return null;
        }

        var eligibility = eligibilityEvaluator.EvaluateCandidateEligibility(system, input);
        var state = eligibility.IsIncompatible
            ? RequirementExperienceLevel2CandidateStates.Incompatible
            : eligibility.RequiresReview
                ? RequirementExperienceLevel2CandidateStates.RequiresReview
                : RequirementExperienceLevel2CandidateStates.Compatible;

        return new RequirementExperienceLevel2SystemCandidateResponse(
            system.Id,
            system.Code,
            system.Name,
            system.TechnicalName,
            system.CommercialName,
            system.FunctionalType,
            system.Family,
            system.Series,
            state,
            Rank: 0,
            eligibility.ReviewReasons,
            eligibility.BlockingReasons);
    }

    private static RequirementExperienceLevel2GlassCandidateResponse MapGlassCandidate(
        GlassTypeCatalogReadModel glass,
        bool hasConfirmedSuggestion,
        IReadOnlyList<string> resolutionReviewReasons)
    {
        var reviewReasons = new List<string>();
        if (glass.RequiresReview)
        {
            reviewReasons.Add("GLASS_REQUIRES_REVIEW");
        }

        if (!hasConfirmedSuggestion)
        {
            reviewReasons.AddRange(resolutionReviewReasons.Count > 0
                ? resolutionReviewReasons
                : ["GLASS_TECHNICAL_RESOLUTION_REQUIRES_REVIEW"]);
        }

        var state = reviewReasons.Count > 0
            ? RequirementExperienceLevel2CandidateStates.RequiresReview
            : RequirementExperienceLevel2CandidateStates.Compatible;

        return new RequirementExperienceLevel2GlassCandidateResponse(
            glass.GlassTypeId,
            glass.Code,
            glass.Name,
            glass.Family,
            glass.Composition,
            glass.Treatment,
            glass.OuterThicknessMm,
            glass.InnerThicknessMm,
            state,
            Rank: 0,
            reviewReasons.Distinct(StringComparer.Ordinal).ToArray(),
            BlockingReasons: []);
    }

    private static RequirementExperienceLevel2SystemCandidateResponse? MapStaleSystemOverride(
        Guid systemId,
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
    {
        var system = systems.FirstOrDefault(value => value.Id == systemId);
        return system is null
            ? null
            : new RequirementExperienceLevel2SystemCandidateResponse(
                system.Id,
                system.Code,
                system.Name,
                system.TechnicalName,
                system.CommercialName,
                system.FunctionalType,
                system.Family,
                system.Series,
                RequirementExperienceLevel2CandidateStates.Incompatible,
                Rank: 0,
                ReviewReasons: [],
                BlockingReasons: ["LEVEL2_OVERRIDE_STALE"]);
    }

    private static RequirementExperienceLevel2GlassCandidateResponse? MapStaleGlassOverride(
        Guid glassTypeId,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses)
    {
        var glass = glasses.FirstOrDefault(value => value.GlassTypeId == glassTypeId);
        return glass is null
            ? null
            : new RequirementExperienceLevel2GlassCandidateResponse(
                glass.GlassTypeId,
                glass.Code,
                glass.Name,
                glass.Family,
                glass.Composition,
                glass.Treatment,
                glass.OuterThicknessMm,
                glass.InnerThicknessMm,
                RequirementExperienceLevel2CandidateStates.Incompatible,
                Rank: 0,
                ReviewReasons: [],
                BlockingReasons: ["LEVEL2_OVERRIDE_STALE"]);
    }

    private static IReadOnlyList<RequirementExperienceLevel2SystemCandidateResponse> OrderSystemCandidates(
        IReadOnlyList<RequirementExperienceLevel2SystemCandidateResponse> candidates,
        string? suggestedCode) =>
        candidates
            .OrderBy(candidate => StatePriority(candidate.CompatibilityState))
            .ThenBy(candidate => SameCode(candidate.Code, suggestedCode) ? 0 : 1)
            .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
            .Select((candidate, index) => candidate with { Rank = index + 1 })
            .ToArray();

    private static IReadOnlyList<RequirementExperienceLevel2GlassCandidateResponse> OrderGlassCandidates(
        IReadOnlyList<RequirementExperienceLevel2GlassCandidateResponse> candidates,
        string? suggestedCode) =>
        candidates
            .OrderBy(candidate => StatePriority(candidate.CompatibilityState))
            .ThenBy(candidate => SameCode(candidate.Code, suggestedCode) ? 0 : 1)
            .ThenBy(candidate => candidate.Code, StringComparer.Ordinal)
            .Select((candidate, index) => candidate with { Rank = index + 1 })
            .ToArray();

    private static bool MatchesLevel1GlassFamily(
        GlassTypeCatalogReadModel glass,
        string family) =>
        family switch
        {
            "TEMPLADO" => Normalize(glass.Family) == "MONOLITHIC"
                && Normalize(glass.Composition) == "TEMPERED",
            "LAMINADO" => Normalize(glass.Family) == "LAMINATED",
            _ => false
        };

    private static int StatePriority(string state) =>
        state switch
        {
            RequirementExperienceLevel2CandidateStates.Compatible => 0,
            RequirementExperienceLevel2CandidateStates.RequiresReview => 1,
            RequirementExperienceLevel2CandidateStates.Incompatible => 2,
            _ => 3
        };

    private static string OverallState(
        string systemState,
        string glassState)
    {
        if (systemState == RequirementExperienceLevel2States.Blocked
            || glassState == RequirementExperienceLevel2States.Blocked)
        {
            return RequirementExperienceLevel2States.Blocked;
        }

        if (systemState == RequirementExperienceLevel2States.Pending
            || glassState == RequirementExperienceLevel2States.Pending)
        {
            return RequirementExperienceLevel2States.Pending;
        }

        if (systemState == RequirementExperienceLevel2States.RequiresReview
            || glassState == RequirementExperienceLevel2States.RequiresReview)
        {
            return RequirementExperienceLevel2States.RequiresReview;
        }

        return RequirementExperienceLevel2States.Resolved;
    }

    private static bool SameCode(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
}
