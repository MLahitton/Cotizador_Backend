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
    IReadOnlyList<RequirementExperienceAnswerResponse> Answers);

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
}

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

public sealed record RequirementExperienceLevel2Response(
    Guid TechnicalProposalId,
    Guid TechnicalProposalItemId,
    string? EffectiveFunctionalType,
    RequirementExperienceLevel2SystemResponse System,
    RequirementExperienceLevel2GlassResponse Glass,
    RequirementExperienceLevel2FinishResponse Finish,
    string OverallState);

public sealed record RequirementExperienceLevel2SystemResponse(
    string State,
    RequirementExperienceLevel2SystemCandidateResponse? Suggested,
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
    IReadOnlyList<string> BlockingReasons);

public sealed record RequirementExperienceLevel2GlassResponse(
    string State,
    string? GlassFamily,
    RequirementExperienceLevel2GlassCandidateResponse? Suggested,
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
    IReadOnlyList<string> BlockingReasons);

public sealed record RequirementExperienceLevel2FinishResponse(
    string State,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons);
