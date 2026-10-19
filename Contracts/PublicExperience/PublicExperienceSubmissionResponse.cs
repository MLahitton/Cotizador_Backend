namespace Contracts.PublicExperience;

public sealed record PublicExperienceSubmissionResponse(
    string SubmissionId, string Status, bool IsReplay, Guid? ClientId = null, Guid? ProjectId = null,
    Guid? PreQuoteId = null, Guid? RequirementId = null);
