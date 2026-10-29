using Domain.PublicExperience;
using Domain.PreQuotes;
using Application.Common.Abstractions.Storage;

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
    Task<PublicPreparationRead> ReadAsync(string submissionId, CancellationToken cancellationToken);
    Task<PublicExperienceRequirementPreparationResult> CommitAsync(PublicPreparationSnapshot snapshot,
        Requirement candidate, IReadOnlyList<PublicPreparedFile> files, CancellationToken cancellationToken);
    Task<PublicExperienceRequirementPreparationResult> FailAsync(PublicPreparationSnapshot snapshot,
        string errorCode, CancellationToken cancellationToken);
}

public sealed record PublicPreparationRead(PublicPreparationSnapshot? Snapshot,
    PublicExperienceRequirementPreparationResult? Result);

public sealed record PublicPreparationSnapshot(Guid Id, string SubmissionId, Guid? ClientId, Guid? ProjectId,
    Guid? OwnerId, string Status, string Fingerprint, string Answers, IReadOnlyList<PublicPreparationFile> Files,
    string? ErrorCode)
{
    public bool Matches(PublicPreparationSnapshot other) => Id == other.Id && SubmissionId == other.SubmissionId
        && ClientId == other.ClientId && ProjectId == other.ProjectId && OwnerId == other.OwnerId
        && Status == other.Status && Fingerprint == other.Fingerprint && Answers == other.Answers
        && ErrorCode == other.ErrorCode && Files.SequenceEqual(other.Files);
}

public sealed record PublicPreparationFile(Guid Id, int Sequence, string OriginalFileName, string ContentType,
    long SizeBytes, string StorageKey, string ContentHash, DateTimeOffset CreatedAtUtc,
    long? StoredLengthBytes, DateTimeOffset? StoredEvidenceAtUtc, short? StoredEvidenceVersion);

public sealed record PublicPreparedFile(Guid SourceId, string StorageKey, StoredContentReceipt Receipt,
    DateTimeOffset EvidenceAtUtc);
