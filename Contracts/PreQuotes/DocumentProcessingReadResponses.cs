namespace Contracts.PreQuotes;

public sealed record DocumentExtractionResultMetadataResponse(
    string SchemaVersion,
    string Classification,
    bool RequiresOcr,
    int PageCount,
    string ProcessingMethod,
    int DurationMs);

public sealed record DocumentProcessingAttemptSummaryResponse(
    Guid ProcessingAttemptId,
    string ProcessingState,
    string? Outcome,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DocumentExtractionResultMetadataResponse? ResultMetadata);

public sealed record PreQuoteDocumentResponse(
    Guid DocumentId,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);
