namespace Domain.PublicExperience;

public static class PublicExperienceSubmissionStatuses
{
    public const string Received = "RECEIVED";
    public const string Processing = "PROCESSING";
    public const string Processed = "PROCESSED";
    public const string Failed = "FAILED";
}

public sealed class PublicExperienceSubmission
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SubmissionId { get; init; }
    public required string SchemaVersion { get; init; }
    public required string Source { get; init; }
    public string Status { get; private set; } = PublicExperienceSubmissionStatuses.Received;
    public required string ClientFullName { get; init; }
    public required string ClientWhatsapp { get; init; }
    public required string AdvisorCode { get; init; }
    public required string AdvisorDisplayName { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectCity { get; init; }
    public string? ProjectSubdivision { get; init; }
    public string? ProjectLotOrApartment { get; init; }
    public string? ProjectNotes { get; init; }
    public required string AnswersJson { get; init; }
    public required string PayloadFingerprint { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? ClientId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? ProcessingErrorCode { get; private set; }
    public Guid? PreQuoteId { get; private set; }
    public Guid? RequirementId { get; private set; }
    public DateTimeOffset? RequirementPreparedAtUtc { get; private set; }
    public string? RequirementPreparationErrorCode { get; private set; }
    public List<PublicExperienceSubmissionFile> Files { get; init; } = [];

    public void CompleteRequirementPreparation(Guid preQuoteId, Guid requirementId, DateTimeOffset now)
    {
        if (ClientId is null || ProjectId is null || PreQuoteId is not null || RequirementId is not null
            || preQuoteId == Guid.Empty || requirementId == Guid.Empty)
            throw new InvalidOperationException("Submission cannot complete requirement preparation.");
        PreQuoteId = preQuoteId;
        RequirementId = requirementId;
        RequirementPreparedAtUtc = now;
        RequirementPreparationErrorCode = null;
        UpdatedAtUtc = now;
    }

    public void FailRequirementPreparation(string errorCode, DateTimeOffset now)
    {
        if (PreQuoteId is not null || RequirementId is not null)
            throw new InvalidOperationException("Linked requirement preparation cannot be failed.");
        RequirementPreparationErrorCode = errorCode;
        UpdatedAtUtc = now;
    }

    public void BeginProcessing(DateTimeOffset now)
    {
        if (Status is not (PublicExperienceSubmissionStatuses.Received or PublicExperienceSubmissionStatuses.Failed)
            || ClientId is not null || ProjectId is not null)
            throw new InvalidOperationException("Submission cannot start processing.");
        Status = PublicExperienceSubmissionStatuses.Processing;
        ProcessingErrorCode = null;
        UpdatedAtUtc = now;
    }

    public void CompleteProcessing(Guid clientId, Guid projectId, DateTimeOffset now)
    {
        if (Status != PublicExperienceSubmissionStatuses.Processing || clientId == Guid.Empty || projectId == Guid.Empty)
            throw new InvalidOperationException("Submission cannot complete processing.");
        ClientId = clientId;
        ProjectId = projectId;
        Status = PublicExperienceSubmissionStatuses.Processed;
        ProcessedAtUtc = now;
        ProcessingErrorCode = null;
        UpdatedAtUtc = now;
    }

    public void FailProcessing(string errorCode, DateTimeOffset now)
    {
        if (Status == PublicExperienceSubmissionStatuses.Processed || ClientId is not null || ProjectId is not null)
            throw new InvalidOperationException("A processed submission cannot be failed.");
        Status = PublicExperienceSubmissionStatuses.Failed;
        ProcessingErrorCode = errorCode;
        UpdatedAtUtc = now;
    }
}

public sealed class PublicExperienceSubmissionFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubmissionId { get; init; }
    public int Sequence { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public required string StorageKey { get; init; }
    public required string ContentHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
