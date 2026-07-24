namespace Contracts.PreQuotes;

public sealed record CreateDocumentProcessingAttemptResponse(
    Guid Id,
    Guid DocumentId,
    Guid CorrelationId,
    string Outcome,
    string? ErrorCode,
    string? SchemaVersion,
    string? Classification,
    bool? RequiresOcr,
    int? PageCount,
    int WarningCount,
    string? ProcessingMethod,
    int? DurationMs,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);
