using Domain.PublicExperience;

namespace Application.PublicExperience;

public static class PublicExperienceRequirementPreparationErrors
{
    public const string NotReady = "PUBLIC_EXPERIENCE_PROJECT_NOT_READY";
    public const string InvalidProject = "PUBLIC_EXPERIENCE_PROJECT_INVALID";
    public const string DocumentsRequired = "PUBLIC_EXPERIENCE_DOCUMENTS_REQUIRED";
    public const string FileIntegrity = "PUBLIC_EXPERIENCE_FILE_INTEGRITY_ERROR";
    public const string FileMetadata = "PUBLIC_EXPERIENCE_FILE_METADATA_INVALID";
    public const string Failed = "PUBLIC_EXPERIENCE_REQUIREMENT_PREPARATION_FAILED";
    public const string InconsistentLinks = "PUBLIC_EXPERIENCE_REQUIREMENT_LINKS_INCONSISTENT";
}

public sealed record PublicExperienceRequirementPreparationResult(
    string SubmissionId, Guid? PreQuoteId, Guid? RequirementId, string? ErrorCode, bool IsReplay = false)
{
    public bool IsSuccess => PreQuoteId is not null && RequirementId is not null && ErrorCode is null;
}

public sealed class PublicExperienceRequirementPreparationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IPublicExperienceRequirementPreparationRepository
{
    // Preparation and repositories share one transaction. Compensate only after known rollback.
    Task<PublicExperienceRequirementPreparationResult?> PrepareLockedAsync(
        string submissionId,
        Func<PublicExperienceSubmission, CancellationToken, Task> prepare,
        Func<Task> compensate,
        CancellationToken cancellationToken);
}
