using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public enum DocumentProcessingAvailability
{
    NotProcessed = 1,
    Pending,
    Processing,
    Failed,
    LegacyOnly,
    AvailableCurrent,
    AvailablePrevious
}

public sealed record DocumentExtractionResultMetadataReadModel(
    string SchemaVersion,
    PdfClassification Classification,
    bool RequiresOcr,
    int PageCount,
    string ProcessingMethod,
    int DurationMs);

public sealed record DocumentProcessingAttemptSummaryReadModel(
    Guid ProcessingAttemptId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome? Outcome,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DocumentExtractionResultMetadataReadModel? ResultMetadata);

public sealed record StructuredExtractionSummaryReadModel(
    Guid StructuredExtractionId,
    Guid SourceProcessingAttemptId,
    bool IsFromLatestAttempt,
    StructuredExtractionStatus Status,
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

public sealed record PreQuoteDocumentListReadModel(
    Guid DocumentId,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    DocumentProcessingAvailability ProcessingAvailability,
    DocumentProcessingAttemptSummaryReadModel? LatestAttempt,
    StructuredExtractionSummaryReadModel? StructuredExtractionSummary);

public sealed record PreQuoteDocumentsPageReadModel(
    IReadOnlyList<PreQuoteDocumentListReadModel> Items,
    int TotalCount);

public sealed record PreQuoteDocumentReadModel(
    Guid DocumentId,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record StructuredEvidenceReadModel(
    int PageNumber,
    EvidenceSourceType SourceType,
    string Text);

public sealed record StructuredProjectReadModel(
    string? Name,
    string? ClientName,
    string? Location,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceReadModel> Evidence);

public sealed record StructuredRequirementReadModel(
    int Sequence,
    RequirementCategory Category,
    string Value,
    IReadOnlyList<StructuredEvidenceReadModel> Evidence);

public sealed record StructuredItemReadModel(
    int Sequence,
    string? Reference,
    string Description,
    StructuredElementType ElementType,
    string? RawMeasurements,
    int? WidthMillimeters,
    int? HeightMillimeters,
    int? Quantity,
    bool RequiresReview,
    IReadOnlyList<StructuredIssueCode> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceReadModel> Evidence);

public sealed record StructuredDocumentReferenceReadModel(
    int Sequence,
    string? Reference,
    string Description,
    string? Detail,
    int? Quantity,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceReadModel> Evidence);

public sealed record StructuredIssueReadModel(
    int Sequence,
    StructuredIssueCode Code,
    string Message,
    int? ItemSequence,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredConflictReadModel(
    int Sequence,
    StructuredConflictCode Code,
    string Message,
    IReadOnlyList<int> ItemSequences,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredSummaryReadModel(
    int ItemCount,
    int DocumentReferenceCount,
    int ItemsRequiringReview,
    int KnownQuoteableUnitCount,
    int IssueCount,
    int ConflictCount);

public sealed record StructuredProcessingMetadataReadModel(
    string Method,
    int DurationMs);

public sealed record StructuredExtractionDetailsReadModel(
    Guid StructuredExtractionId,
    Guid SourceProcessingAttemptId,
    bool IsFromLatestAttempt,
    StructuredExtractionStatus Status,
    StructuredProjectReadModel Project,
    IReadOnlyList<StructuredRequirementReadModel> Requirements,
    IReadOnlyList<StructuredItemReadModel> Items,
    IReadOnlyList<StructuredDocumentReferenceReadModel> DocumentReferences,
    IReadOnlyList<StructuredIssueReadModel> Issues,
    IReadOnlyList<StructuredConflictReadModel> Conflicts,
    StructuredSummaryReadModel Summary,
    StructuredProcessingMetadataReadModel ProcessingMetadata,
    DateTimeOffset CreatedAtUtc);

public sealed record StructuredDocumentExtractionQueryReadModel(
    PreQuoteDocumentReadModel Document,
    DocumentProcessingAvailability ProcessingAvailability,
    DocumentProcessingAttemptSummaryReadModel? LatestAttempt,
    StructuredExtractionDetailsReadModel? StructuredExtraction);

public interface IPreQuoteDocumentQueryRepository
{
    Task<PreQuoteDocumentsPageReadModel?> GetDocumentsAsync(
        Guid preQuoteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<StructuredDocumentExtractionQueryReadModel?> GetStructuredExtractionAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed class PreQuoteDocumentQueryException : Exception
{
    public PreQuoteDocumentQueryException(Exception innerException)
        : base("No fue posible consultar los documentos.", innerException)
    {
    }
}
