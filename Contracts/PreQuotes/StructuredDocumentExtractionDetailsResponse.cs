namespace Contracts.PreQuotes;

public sealed record StructuredEvidenceResponse(
    int PageNumber,
    string SourceType,
    string Text);

public sealed record StructuredProjectResponse(
    string? Name,
    string? ClientName,
    string? Location,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceResponse> Evidence);

public sealed record StructuredRequirementResponse(
    int Sequence,
    string Category,
    string Value,
    IReadOnlyList<StructuredEvidenceResponse> Evidence);

public sealed record StructuredItemResponse(
    int Sequence,
    string? Reference,
    string Description,
    string ElementType,
    string? RawMeasurements,
    int? WidthMillimeters,
    int? HeightMillimeters,
    int? Quantity,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceResponse> Evidence,
    StructuredExtractionItemGlassResponse? Glass,
    StructuredExtractionItemGlassValuationResponse? Valuation = null);

public sealed record StructuredExtractionItemGlassValuationResponse(
    string Status,
    string? Reason,
    Guid? GlassTypeId,
    Guid? GlassPriceRangeVersionId,
    int? PriceRangeVersion,
    string? PriceRangeStatus,
    string? Currency,
    decimal? UnitAreaSquareMeters,
    decimal? TotalAreaSquareMeters,
    decimal? MinimumPricePerSquareMeter,
    decimal? MaximumPricePerSquareMeter,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    DateTimeOffset CalculatedAtUtc);

public sealed record StructuredExtractionItemGlassEvidenceResponse(
    int PageNumber,
    string SourceType,
    string Text);

public sealed record StructuredExtractionItemGlassResponse(
    Guid? GlassTypeId,
    string? RawSpecification,
    string? NormalizedCode,
    string AssignmentScope,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredExtractionItemGlassEvidenceResponse> Evidence);

public sealed record StructuredDocumentReferenceResponse(
    int Sequence,
    string? Reference,
    string Description,
    string? Detail,
    int? Quantity,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredEvidenceResponse> Evidence);

public sealed record StructuredIssueResponse(
    int Sequence,
    string Code,
    string Message,
    int? ItemSequence,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredConflictResponse(
    int Sequence,
    string Code,
    string Message,
    IReadOnlyList<int> ItemSequences,
    IReadOnlyList<int> PageNumbers);

public sealed record StructuredSummaryResponse(
    int ItemCount,
    int DocumentReferenceCount,
    int ItemsRequiringReview,
    int KnownQuoteableUnitCount,
    int IssueCount,
    int ConflictCount,
    int? IdentifiedGlassItemCount,
    int? GlassItemsRequiringReview,
    int ValuedItemCount = 0,
    int NotValuedItemCount = 0,
    decimal TotalGlassAreaSquareMeters = 0,
    decimal? MinimumGlassAmount = null,
    decimal? MaximumGlassAmount = null,
    string? Currency = null,
    bool IsAggregable = true,
    string? AggregationIssue = null);

public sealed record StructuredProcessingMetadataResponse(
    string Method,
    int DurationMs);

public sealed record StructuredExtractionDetailsResponse(
    Guid StructuredExtractionId,
    Guid SourceProcessingAttemptId,
    bool IsFromLatestAttempt,
    string Status,
    StructuredProjectResponse Project,
    IReadOnlyList<StructuredRequirementResponse> Requirements,
    IReadOnlyList<StructuredItemResponse> Items,
    IReadOnlyList<StructuredDocumentReferenceResponse> DocumentReferences,
    IReadOnlyList<StructuredIssueResponse> Issues,
    IReadOnlyList<StructuredConflictResponse> Conflicts,
    StructuredSummaryResponse Summary,
    StructuredProcessingMetadataResponse ProcessingMetadata,
    DateTimeOffset CreatedAtUtc);

public sealed record StructuredDocumentExtractionDetailsResponse(
    PreQuoteDocumentResponse Document,
    string ProcessingAvailability,
    DocumentProcessingAttemptSummaryResponse? LatestAttempt,
    StructuredExtractionDetailsResponse? StructuredExtraction);
