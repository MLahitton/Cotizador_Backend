namespace Contracts.PreQuotes;

public sealed record CreatePreQuoteDraftRequest(
    Guid SourceDocumentId,
    Guid SourceStructuredExtractionId);

public sealed record ApprovePreQuoteDraftRequest(int ExpectedVersion);

public sealed record UpdatePreQuoteDraftRequest(
    int ExpectedVersion,
    PreQuoteDraftProjectRequest Project,
    IReadOnlyList<PreQuoteDraftItemRequest> Items,
    IReadOnlyList<PreQuoteDraftRequirementRequest> Requirements,
    IReadOnlyList<PreQuoteDraftDocumentReferenceRequest> DocumentReferences,
    IReadOnlyList<PreQuoteDraftIssueResolutionRequest> Issues,
    IReadOnlyList<PreQuoteDraftConflictResolutionRequest> Conflicts);

public sealed record PreQuoteDraftProjectRequest(
    string? Name, string? ClientName, string? Location);

public sealed record PreQuoteDraftItemRequest(
    Guid? DraftItemId,
    int Sequence,
    string? Reference,
    string Description,
    string ElementType,
    string? RawMeasurements,
    int? WidthMillimeters,
    int? HeightMillimeters,
    int? Quantity,
    bool IsIncluded);

public sealed record PreQuoteDraftRequirementRequest(
    Guid? DraftRequirementId,
    int Sequence,
    string Category,
    string Value,
    bool IsIncluded);

public sealed record PreQuoteDraftDocumentReferenceRequest(
    Guid? DraftDocumentReferenceId,
    int Sequence,
    string? Reference,
    string Description,
    string? Detail,
    int? Quantity,
    bool IsIncluded);

public sealed record PreQuoteDraftIssueResolutionRequest(
    Guid DraftIssueId,
    string ResolutionStatus,
    string? ResolutionNote);

public sealed record PreQuoteDraftConflictResolutionRequest(
    Guid DraftConflictId,
    string ResolutionStatus,
    string? ResolutionNote);

public sealed record PreQuoteDraftDetailsResponse(
    Guid Id,
    Guid PreQuoteId,
    Guid SourceDocumentId,
    Guid SourceStructuredExtractionId,
    string Status,
    int Version,
    string? ProjectName,
    string? ClientName,
    string? Location,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    IReadOnlyList<PreQuoteDraftItemResponse> Items,
    IReadOnlyList<PreQuoteDraftRequirementResponse> Requirements,
    IReadOnlyList<PreQuoteDraftDocumentReferenceResponse> DocumentReferences,
    IReadOnlyList<PreQuoteDraftIssueResponse> Issues,
    IReadOnlyList<PreQuoteDraftConflictResponse> Conflicts,
    PreQuoteDraftEconomicSummaryResponse EconomicSummary,
    PreQuoteDraftSummaryResponse? Summary,
    PreQuoteDraftAuditResponse? Audit);

public sealed record PreQuoteDraftItemResponse(
    Guid Id,
    int Sequence,
    string Origin,
    Guid? SourceStructuredItemId,
    int? SourceItemSequence,
    string? Reference,
    string Description,
    string ElementType,
    string? RawMeasurements,
    int? WidthMillimeters,
    int? HeightMillimeters,
    int? Quantity,
    bool IsIncluded,
    PreQuoteDraftItemGlassResponse? Glass,
    PreQuoteDraftItemValuationResponse? Valuation,
    PreQuoteDraftItemTechnicalSnapshotResponse? TechnicalSnapshot = null)
{
    public int? SourceSequence => SourceItemSequence;
}

public sealed record PreQuoteDraftItemTechnicalSnapshotResponse(
    Guid SourceStructuredItemTechnicalClassificationId,
    string? SystemCode,
    string? SystemOriginalText,
    string? SystemSource,
    decimal? SystemConfidence,
    string? FrameCode,
    string? FrameOriginalText,
    string? FrameSource,
    decimal? FrameConfidence,
    string? FinishCode,
    string? FinishOriginalText,
    string? FinishSource,
    decimal? FinishConfidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons);

public sealed record PreQuoteDraftItemGlassResponse(
    Guid? SourceStructuredItemGlassId,
    Guid? GlassTypeId,
    string? RawSpecification,
    string? NormalizedCodeSnapshot,
    string AssignmentScope,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<PreQuoteDraftItemGlassEvidenceResponse> Evidence);

public sealed record PreQuoteDraftItemGlassEvidenceResponse(
    int Sequence,
    int PageNumber,
    string SourceType,
    string Text);

public sealed record PreQuoteDraftItemValuationResponse(
    Guid SourceStructuredItemValuationId,
    string Status,
    string? Reason,
    Guid? GlassTypeId,
    Guid? GlassPriceRangeVersionId,
    int? WidthMillimetersUsed,
    int? HeightMillimetersUsed,
    int? QuantityUsed,
    decimal? UnitAreaSquareMeters,
    decimal? TotalAreaSquareMeters,
    decimal? UnitPricePerSquareMeter,
    decimal? UnitAmount,
    decimal? TotalAmount,
    string? Currency,
    DateTimeOffset ValuedAtUtc,
    DateTimeOffset? InvalidatedAtUtc,
    string? InvalidationReason,
    decimal? BillableAreaUnitSquareMeters = null,
    int? GlassPriceRangeVersion = null,
    decimal? GlassMinimumPricePerSquareMeter = null,
    decimal? GlassExpectedPricePerSquareMeter = null,
    decimal? GlassMaximumPricePerSquareMeter = null,
    string? SystemCode = null,
    string? SystemSource = null,
    string? FrameCode = null,
    string? FinishCode = null,
    string? LaborProfileCode = null,
    string? AssemblyProfileCode = null,
    decimal? FinishFactorMinimum = null,
    decimal? FinishFactorExpected = null,
    decimal? FinishFactorMaximum = null,
    decimal? AccessoryFactor = null,
    decimal? GlassMinimumAmount = null,
    decimal? GlassExpectedAmount = null,
    decimal? GlassMaximumAmount = null,
    decimal? LaborMinimumAmount = null,
    decimal? LaborExpectedAmount = null,
    decimal? LaborMaximumAmount = null,
    decimal? AssemblyMinimumAmount = null,
    decimal? AssemblyExpectedAmount = null,
    decimal? AssemblyMaximumAmount = null,
    decimal? AccessoriesMinimumAmount = null,
    decimal? AccessoriesExpectedAmount = null,
    decimal? AccessoriesMaximumAmount = null,
    decimal? ItemMinimumAmount = null,
    decimal? ItemExpectedAmount = null,
    decimal? ItemMaximumAmount = null,
    string? PricingProfileVersion = null,
    int? ConfidenceScore = null,
    string? ConfidenceLevel = null,
    IReadOnlyList<string>? Assumptions = null,
    IReadOnlyList<string>? MissingData = null,
    bool? RequiresReview = null,
    DateTimeOffset? CalculatedAtUtc = null);

public sealed record PreQuoteDraftRequirementResponse(
    Guid DraftRequirementId,
    int Sequence,
    string Origin,
    int? SourceSequence,
    string Category,
    string Value,
    bool IsIncluded);

public sealed record PreQuoteDraftDocumentReferenceResponse(
    Guid DraftDocumentReferenceId,
    int Sequence,
    string Origin,
    int? SourceSequence,
    string? Reference,
    string Description,
    string? Detail,
    int? Quantity,
    bool IsIncluded);

public sealed record PreQuoteDraftIssueResponse(
    Guid DraftIssueId,
    int Sequence,
    int? SourceSequence,
    string Code,
    string Message,
    int? ItemSequence,
    IReadOnlyList<int> PageNumbers,
    string ResolutionStatus,
    string? ResolutionNote,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc);

public sealed record PreQuoteDraftConflictResponse(
    Guid DraftConflictId,
    int Sequence,
    int SourceSequence,
    string Code,
    string Message,
    IReadOnlyList<int> ItemSequences,
    IReadOnlyList<int> PageNumbers,
    string ResolutionStatus,
    string? ResolutionNote,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc);

public sealed record PreQuoteDraftSummaryResponse(
    int TotalItemCount,
    int IncludedItemCount,
    int ExcludedItemCount,
    int ManualItemCount,
    int ItemsRequiringCompletion,
    long IncludedKnownQuoteableUnitCount,
    int TotalRequirementCount,
    int IncludedRequirementCount,
    int TotalDocumentReferenceCount,
    int IncludedDocumentReferenceCount,
    int PendingIssueCount,
    int ResolvedIssueCount,
    int DismissedIssueCount,
    int PendingConflictCount,
    int ResolvedConflictCount,
    int DismissedConflictCount);

public sealed record PreQuoteDraftEconomicSummaryResponse(
    int IncludedItemCount,
    int IncludedKnownQuoteableUnitCount,
    int ValuedItemCount,
    int PendingValuationItemCount,
    int StaleValuationItemCount,
    int NotPriceableItemCount,
    int ItemsRequiringReviewCount,
    decimal? TotalAreaSquareMeters,
    decimal? GlassSubtotal,
    string? Currency,
    bool IsEconomicallyComplete,
    decimal? MinimumTechnicalSubtotal = null,
    decimal? ExpectedTechnicalSubtotal = null,
    decimal? MaximumTechnicalSubtotal = null,
    decimal? TransportMinimum = null,
    decimal? TransportExpected = null,
    decimal? TransportMaximum = null,
    decimal? AdministrationMinimum = null,
    decimal? AdministrationExpected = null,
    decimal? AdministrationMaximum = null,
    decimal? ContingencyMinimum = null,
    decimal? ContingencyExpected = null,
    decimal? ContingencyMaximum = null,
    decimal? ProfitMinimum = null,
    decimal? ProfitExpected = null,
    decimal? ProfitMaximum = null,
    decimal? VatMinimum = null,
    decimal? VatExpected = null,
    decimal? VatMaximum = null,
    decimal? FinalMinimum = null,
    decimal? FinalExpected = null,
    decimal? FinalMaximum = null,
    int? OverallConfidence = null,
    string? ConfidenceLevel = null,
    IReadOnlyList<string>? Assumptions = null,
    IReadOnlyList<string>? MissingData = null,
    bool HasLimitedPricingScope = false)
{
    public bool HasNotPriceableItems => NotPriceableItemCount > 0;
}

public sealed record PreQuoteDraftAuditResponse(
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    Guid? ApprovedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ApprovedAtUtc);
