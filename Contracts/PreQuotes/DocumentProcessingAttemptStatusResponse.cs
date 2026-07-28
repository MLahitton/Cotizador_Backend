using System.Text.Json;

namespace Contracts.PreQuotes;

public sealed record DocumentProcessingAttemptStatusResponse(
    Guid ProcessingAttemptId,
    Guid DocumentId,
    string ProcessingState,
    string? Outcome,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    JsonElement? Result);
