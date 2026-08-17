using Domain.PreQuotes;

namespace Application.Common.Abstractions.DocumentProcessing;

public interface IDocumentProcessingClient
{
    Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken);
}

public interface IAi2DocumentProcessingClient : IDocumentProcessingClient;

public interface IDocumentProcessingDiagnostics
{
    void ContractRejected(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        int? httpStatusCode,
        string stage,
        string category,
        int? itemSequence = null,
        string? rejectedNormalizedCode = null,
        IReadOnlyList<string>? acceptedNormalizedCodes = null,
        string? exceptionType = null,
        string? exceptionMessage = null,
        string? jsonPath = null,
        string? fieldName = null,
        string? rejectedValue = null,
        long? lineNumber = null,
        long? bytePositionInLine = null,
        IReadOnlyList<string>? acceptedValues = null);

    void CatalogResolutionFailed(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        string category,
        string? normalizedCode,
        int? itemSequence = null,
        IReadOnlyList<string>? acceptedNormalizedCodes = null);
}

public sealed record DocumentProcessingFile(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

public sealed record DocumentProcessingClientRequest
{
    public DocumentProcessingClientRequest(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        IReadOnlyList<DocumentProcessingFile> files,
        Guid? projectId = null,
        Guid? requirementId = null)
    {
        DocumentId = documentId;
        ProcessingAttemptId = processingAttemptId;
        CorrelationId = correlationId;
        Files = files;
        ProjectId = projectId;
        RequirementId = requirementId;
    }

    public DocumentProcessingClientRequest(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content)
        : this(
            documentId,
            processingAttemptId,
            correlationId,
            [new DocumentProcessingFile(
                documentId, fileName, contentType, sizeBytes, content)])
    {
    }

    public Guid DocumentId { get; }
    public Guid ProcessingAttemptId { get; }
    public Guid CorrelationId { get; }
    public IReadOnlyList<DocumentProcessingFile> Files { get; }
    public Guid? ProjectId { get; }
    public Guid? RequirementId { get; }

    public DocumentProcessingFile PrimaryFile => Files[0];
    public string FileName => PrimaryFile.FileName;
    public string ContentType => PrimaryFile.ContentType;
    public long SizeBytes => PrimaryFile.SizeBytes;
    public Stream Content => PrimaryFile.Content;
}

public enum DocumentProcessingProvider
{
    LegacyAi = 1,
    Ai2 = 2
}

public enum CanonicalExtractionValueStatus
{
    Explicit = 1,
    Inferred = 2,
    Ambiguous = 3,
    Unknown = 4,
    NotApplicable = 5
}

public enum DocumentProcessingClientFailure
{
    None = 0,
    RemoteRejection = 1,
    ServiceUnavailable = 2,
    Timeout = 3,
    InvalidResponse = 4,
    ServiceError = 5
}

public sealed record DocumentProcessingRemoteError(
    int StatusCode,
    string SchemaVersion,
    string ErrorCode,
    string Message);

public sealed record ProcessedDocumentData(
    string FileName,
    string ContentType,
    long SizeBytes,
    int PageCount,
    DocumentClassification Classification,
    bool RequiresOcr);

public sealed record ProcessedPageData(
    int PageNumber,
    string Text,
    int CharacterCount,
    bool HasExtractableText);

public sealed record ProcessingWarningData(
    string Code,
    string Message,
    IReadOnlyList<int> PageNumbers);

public sealed record ProcessingMetadataData(
    string Method,
    int DurationMs);

public sealed record SourceEvidenceData(
    int? PageNumber,
    EvidenceSourceType SourceType,
    string Text,
    string? SheetName = null,
    string? CellRange = null,
    string? SourceId = null,
    decimal? Confidence = null,
    CanonicalExtractionValueStatus Status =
        CanonicalExtractionValueStatus.Explicit);

public sealed record StructuredItemTechnicalClassificationData(
    string? SystemCode,
    string? SystemOriginalText,
    TechnicalClassificationSource? SystemSource,
    decimal? SystemConfidence,
    string? FrameCode,
    string? FrameOriginalText,
    TechnicalClassificationSource? FrameSource,
    decimal? FrameConfidence,
    string? FinishCode,
    string? FinishOriginalText,
    TechnicalClassificationSource? FinishSource,
    decimal? FinishConfidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons);

public sealed record StructuredRequirementData(
    RequirementCategory Category,
    string Value,
    IReadOnlyList<SourceEvidenceData> Evidence);

public sealed record StructuredItemData(
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
    IReadOnlyList<SourceEvidenceData> Evidence,
    StructuredItemGlassData? Glass = null,
    StructuredItemTechnicalClassificationData? TechnicalClassification = null,
    decimal? AreaSquareMeters = null,
    string? Configuration = null,
    decimal? Confidence = null,
    CanonicalExtractionValueStatus ExtractionStatus =
        CanonicalExtractionValueStatus.Explicit);

public sealed record StructuredItemGlassData(
    string? RawSpecification,
    string? NormalizedCode,
    GlassAssignmentScope AssignmentScope,
    bool RequiresReview,
    IReadOnlyList<GlassReviewReason> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<SourceEvidenceData> Evidence);

public sealed record StructuredDocumentReferenceData(
    int Sequence,
    string? Reference,
    string Description,
    string? Detail,
    int? Quantity,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<SourceEvidenceData> Evidence);

public sealed record StructuredIssueData(
    int Sequence,
    StructuredIssueCode Code,
    string Message,
    int? ItemSequence,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredConflictData(
    int Sequence,
    StructuredConflictCode Code,
    string Message,
    IReadOnlyList<int> ItemSequences,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredExtractionData(
    StructuredExtractionStatus Status,
    string? ProjectName,
    string? ClientName,
    string? Location,
    IReadOnlyList<int> ProjectSourcePages,
    IReadOnlyList<SourceEvidenceData> ProjectEvidence,
    IReadOnlyList<StructuredRequirementData> Requirements,
    IReadOnlyList<StructuredItemData> Items,
    IReadOnlyList<StructuredDocumentReferenceData> DocumentReferences,
    IReadOnlyList<StructuredIssueData> Issues,
    IReadOnlyList<StructuredConflictData> Conflicts,
    int ItemCount,
    int DocumentReferenceCount,
    int ItemsRequiringReview,
    int KnownQuoteableUnitCount,
    string ProcessingMethod,
    int DurationMs,
    int? IdentifiedGlassItemCount = null,
    int? GlassItemsRequiringReview = null);

public sealed record DocumentProcessingResponseData(
    string SchemaVersion,
    Guid DocumentId,
    Guid ProcessingAttemptId,
    DocumentProcessingOutcome Outcome,
    ProcessedDocumentData Document,
    IReadOnlyList<ProcessedPageData> Pages,
    IReadOnlyList<ProcessingWarningData> Warnings,
    ProcessingMetadataData ProcessingMetadata,
    string PayloadJson,
    StructuredExtractionData? StructuredExtraction = null,
    DocumentProcessingProvider Provider =
        DocumentProcessingProvider.LegacyAi,
    bool RequiresResolvedGlassCatalog = false,
    bool SupportsPreliminaryValuation = false);

public sealed record DocumentProcessingClientResult(
    DocumentProcessingClientFailure Failure,
    DocumentProcessingResponseData? Response,
    DocumentProcessingRemoteError? RemoteError)
{
    public bool IsSuccess =>
        Failure == DocumentProcessingClientFailure.None
        && Response is not null
        && RemoteError is null;

    public static DocumentProcessingClientResult Success(
        DocumentProcessingResponseData response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new DocumentProcessingClientResult(
            DocumentProcessingClientFailure.None,
            response,
            null);
    }

    public static DocumentProcessingClientResult Failed(
        DocumentProcessingClientFailure failure)
    {
        if (failure is DocumentProcessingClientFailure.None
            or DocumentProcessingClientFailure.RemoteRejection
            or DocumentProcessingClientFailure.ServiceError)
        {
            throw new ArgumentException(
                "El failure indicado no es válido para un resultado sin error remoto.",
                nameof(failure));
        }

        return new DocumentProcessingClientResult(failure, null, null);
    }

    public static DocumentProcessingClientResult RemoteFailure(
        DocumentProcessingClientFailure failure,
        DocumentProcessingRemoteError remoteError)
    {
        if (failure is not DocumentProcessingClientFailure.RemoteRejection
            and not DocumentProcessingClientFailure.ServiceError)
        {
            throw new ArgumentException(
                "El failure indicado no admite un error remoto.",
                nameof(failure));
        }

        ArgumentNullException.ThrowIfNull(remoteError);

        return new DocumentProcessingClientResult(
            failure,
            null,
            remoteError);
    }
}
