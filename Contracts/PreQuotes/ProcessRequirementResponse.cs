namespace Contracts.PreQuotes;

public sealed record ProcessRequirementSummaryResponse(
    int ItemCount,
    int ItemsRequiringReview,
    int IssueCount,
    int ConflictCount,
    string ProcessingMethod,
    int DurationMs);

public sealed record ProcessRequirementResponse(
    Guid RequirementId,
    Guid ProcessingAttemptId,
    Guid CorrelationId,
    string ProcessingState,
    string Outcome,
    string? ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ProcessRequirementSummaryResponse? Summary);
