namespace Contracts.PreQuotes;

public sealed record StructuredExtractionSummaryResponse(
    Guid StructuredExtractionId,
    Guid SourceProcessingAttemptId,
    bool IsFromLatestAttempt,
    string Status,
    string? ProjectName,
    string? ClientName,
    string? Location,
    int ItemCount,
    int DocumentReferenceCount,
    int ItemsRequiringReview,
    int KnownQuoteableUnitCount,
    int IssueCount,
    int ConflictCount,
    string ProcessingMethod,
    int DurationMs,
    DateTimeOffset CreatedAtUtc);

public sealed record PreQuoteDocumentListItemResponse(
    Guid DocumentId,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    string ProcessingAvailability,
    DocumentProcessingAttemptSummaryResponse? LatestAttempt,
    StructuredExtractionSummaryResponse? StructuredExtractionSummary);

public sealed record GetPreQuoteDocumentsResponse(
    IReadOnlyList<PreQuoteDocumentListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
