using Domain.PreQuotes;

namespace Application.PreQuotes;

public sealed record DocumentProcessingAttemptStatusData(
    Guid ProcessingAttemptId,
    Guid DocumentId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome? Outcome,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultPayloadJson);
