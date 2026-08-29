using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public enum ProcessRequirementFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    RequirementNotFound = 4,
    PreQuoteNotFound = 5,
    ProjectNotFound = 6,
    InactiveProject = 7,
    ClientNotFound = 8,
    InactiveClient = 9,
    AlreadyProcessing = 10,
    NoFiles = 11,
    QueryError = 12,
    StorageError = 13,
    AiServiceUnavailable = 14,
    AiTimeout = 15,
    AiRemoteRejected = 16,
    AiInvalidResponse = 17,
    AiServiceError = 18,
    PersistenceError = 19,
    Cancelled = 20
}

public sealed record ProcessedRequirementSummary(
    int ItemCount,
    int ItemsRequiringReview,
    int IssueCount,
    int ConflictCount,
    string ProcessingMethod,
    int DurationMs);

public sealed record ProcessedRequirementAttemptResult(
    Guid RequirementId,
    Guid ProcessingAttemptId,
    Guid CorrelationId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome Outcome,
    string? ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ProcessedRequirementSummary? Summary);

public sealed record ProcessRequirementResult(
    bool IsSuccess,
    ProcessedRequirementAttemptResult? Attempt,
    ProcessRequirementFailure Failure)
{
    public static ProcessRequirementResult Success(
        ProcessedRequirementAttemptResult attempt)
    {
        return new ProcessRequirementResult(
            true,
            attempt,
            ProcessRequirementFailure.None);
    }

    public static ProcessRequirementResult Failed(
        ProcessRequirementFailure failure,
        ProcessedRequirementAttemptResult? attempt = null)
    {
        return new ProcessRequirementResult(false, attempt, failure);
    }
}
