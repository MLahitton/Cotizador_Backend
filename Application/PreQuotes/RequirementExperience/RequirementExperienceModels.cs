namespace Application.PreQuotes.RequirementExperience;

public sealed record RequirementExperienceCatalogResponse(
    string Version,
    IReadOnlyList<RequirementExperienceQuestionResponse> Questions,
    IReadOnlyList<RequirementExperienceSpaceResponse> Spaces);

public sealed record RequirementExperienceQuestionResponse(
    string BenefitCode,
    string Label,
    string Question,
    IReadOnlyList<RequirementExperienceOptionResponse> Options);

public sealed record RequirementExperienceOptionResponse(
    string OptionCode,
    string OptionLabel,
    string ShortLabel,
    IReadOnlyList<string> Conditions);

public sealed record RequirementExperienceSpaceResponse(
    string Code,
    string Label,
    IReadOnlyDictionary<string, int> Priorities);

public sealed record RequirementExperienceDraftsResponse(
    Guid TechnicalProposalId,
    IReadOnlyList<RequirementExperienceItemDraftResponse> Items);

public sealed record RequirementExperienceItemDraftResponse(
    Guid TechnicalProposalItemId,
    string? CatalogVersion,
    string? SpaceTypeCode,
    string ResolutionState,
    long Revision,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    RequirementExperienceLevel1ResolutionResponse? Level1Resolution,
    IReadOnlyList<RequirementExperienceAnswerResponse> Answers)
{
    public bool HasSavedDraft => Revision > 0;
    public string PreferenceOrigin { get; init; } = "NONE";
}

public sealed record RequirementExperienceLevel1ResolutionResponse(
    string? SystemTier,
    string? GlassFamily,
    bool IsComplete,
    int AnsweredBenefits,
    int RequiredBenefits);

public sealed record RequirementExperienceAnswerResponse(
    string BenefitCode,
    string OptionCode);

public sealed record UpdateRequirementExperienceDraftRequest(
    string CatalogVersion,
    string? SpaceTypeCode,
    long ExpectedRevision,
    IReadOnlyList<UpdateRequirementExperienceAnswerRequest> Answers);

public sealed record UpdateRequirementExperienceAnswerRequest(
    string BenefitCode,
    string OptionCode);

public sealed record UpdateRequirementExperienceLevel2SystemRequest(
    Guid? SystemId,
    long? ExpectedRevision);

public sealed record UpdateRequirementExperienceLevel2GlassRequest(
    Guid? GlassId,
    long? ExpectedRevision);

public sealed record RequirementExperienceResult<T>(
    bool IsSuccess,
    T? Value,
    RequirementExperienceFailure Error)
{
    public static RequirementExperienceResult<T> Success(T value)
    {
        return new RequirementExperienceResult<T>(
            true,
            value,
            RequirementExperienceFailure.None);
    }

    public static RequirementExperienceResult<T> Failed(
        RequirementExperienceFailure failure)
    {
        return new RequirementExperienceResult<T>(
            false,
            default,
            failure);
    }

    public static RequirementExperienceResult<T> Failed(
        RequirementExperienceFailure failure,
        T? value)
    {
        return new RequirementExperienceResult<T>(
            false,
            value,
            failure);
    }
}

public static class RequirementExperienceConfigurationStatuses
{
    public const string Draft = "DRAFT";
    public const string Confirmed = "CONFIRMED";
    public const string Stale = "STALE";
}

public sealed record RequirementExperienceConfigurationStatusResponse(
    Guid TechnicalProposalId,
    string Status,
    bool CanConfirm,
    long? LatestConfirmationRevision,
    DateTimeOffset? ConfirmedAtUtc,
    Guid? ConfirmedByUserId,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<RequirementExperienceConfigurationItemStatusResponse> Items);

public sealed record RequirementExperienceConfigurationItemStatusResponse(
    Guid TechnicalProposalItemId,
    string? Reference,
    int? Sequence,
    bool IsIncluded,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    decimal? AreaM2,
    string? Location,
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId,
    string? SystemState,
    string? GlassState,
    string? FinishState,
    string? SystemOverrideState,
    string? GlassOverrideState,
    string ReadinessState,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons,
    string? QuantitySource = null,
    string? QuantityExtractionStatus = null,
    decimal? QuantityExtractionConfidence = null);

public static class RequirementExperienceLevel2States
{
    public const string Resolved = "RESOLVED";
    public const string RequiresReview = "REQUIRES_REVIEW";
    public const string Pending = "PENDING";
    public const string Blocked = "BLOCKED";
    public const string PendingExternalSource = "PENDING_EXTERNAL_SOURCE";
}

public static class RequirementExperienceLevel2CandidateStates
{
    public const string Compatible = "COMPATIBLE";
    public const string RequiresReview = "REQUIRES_REVIEW";
    public const string Incompatible = "INCOMPATIBLE";
}

public static class RequirementExperienceLevel2OverrideStates
{
    public const string None = "NONE";
    public const string Valid = "VALID";
    public const string RequiresReview = "REQUIRES_REVIEW";
    public const string Stale = "STALE";
}

public sealed record RequirementExperienceLevel2Response(
    Guid TechnicalProposalId,
    Guid TechnicalProposalItemId,
    long? TechnicalSelectionRevision,
    string? EffectiveFunctionalType,
    RequirementExperienceLevel2SystemResponse System,
    RequirementExperienceLevel2GlassResponse Glass,
    RequirementExperienceLevel2FinishResponse Finish,
    string OverallState);

public sealed record RequirementExperienceLevel2SystemResponse(
    string State,
    RequirementExperienceLevel2SystemCandidateResponse? Suggested,
    RequirementExperienceLevel2SystemCandidateResponse? Override,
    RequirementExperienceLevel2SystemCandidateResponse? Effective,
    string OverrideState,
    IReadOnlyList<RequirementExperienceLevel2SystemCandidateResponse> Candidates,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons);

public sealed record RequirementExperienceLevel2SystemCandidateResponse(
    Guid Id,
    string Code,
    string Name,
    string? TechnicalName,
    string? CommercialName,
    string? FunctionalType,
    string? Family,
    string? Series,
    string CompatibilityState,
    int Rank,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons) : IRequirementExperienceLevel2Candidate;

public sealed record RequirementExperienceLevel2GlassResponse(
    string State,
    string? GlassFamily,
    RequirementExperienceLevel2GlassCandidateResponse? Suggested,
    RequirementExperienceLevel2GlassCandidateResponse? Override,
    RequirementExperienceLevel2GlassCandidateResponse? Effective,
    string OverrideState,
    IReadOnlyList<RequirementExperienceLevel2GlassCandidateResponse> Candidates,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons);

public sealed record RequirementExperienceLevel2GlassCandidateResponse(
    Guid Id,
    string Code,
    string Name,
    string? Family,
    string? Composition,
    string? Treatment,
    decimal? OuterThicknessMm,
    decimal? InnerThicknessMm,
    string CompatibilityState,
    int Rank,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons) : IRequirementExperienceLevel2Candidate;

public sealed record RequirementExperienceLevel2FinishResponse(
    string State,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons);

public interface IRequirementExperienceLevel2Candidate
{
    Guid Id { get; }

    string CompatibilityState { get; }
}
