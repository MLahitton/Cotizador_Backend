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
