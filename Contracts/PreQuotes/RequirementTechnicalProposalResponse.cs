namespace Contracts.PreQuotes;

public sealed record RequirementTechnicalProposalResponse(
    Guid RequirementId,
    Guid TechnicalProposalId,
    Guid ProcessingAttemptId,
    Guid ExtractionResultId,
    string Status,
    string? CommercialLine,
    RequirementTechnicalProposalCommercialConfirmationResponse
        CommercialConfirmation,
    DateTimeOffset CreatedAtUtc,
    int ItemCount,
    int DetectedItemCount,
    int ManualItemCount,
    int TotalProposalItemCount,
    int ItemsRequiringReview,
    int TechnicallyCompleteItems,
    int PriceableItems,
    RequirementTechnicalProposalReadinessResponse Readiness,
    IReadOnlyList<RequirementTechnicalProposalItemResponse> Items);

public sealed record RequirementTechnicalProposalCommercialConfirmationResponse(
    string State,
    DateTimeOffset? ConfirmedAtUtc,
    Guid? ConfirmedByUserId);

public sealed record RequirementTechnicalProposalItemResponse(
    Guid ItemId,
    Guid? ExtractedItemId,
    string Source,
    string? ElementId,
    int Sequence,
    string? Reference,
    string Description,
    string ElementType,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    int? ManualQuantityOverride,
    int? ManualWidthMmOverride,
    int? ManualHeightMmOverride,
    int? EffectiveQuantity,
    int? EffectiveWidthMm,
    int? EffectiveHeightMm,
    decimal? AreaM2,
    bool IsIncluded,
    DateTimeOffset? ExcludedAtUtc,
    Guid? ExcludedByUserId,
    string? ExclusionReason,
    decimal? ExtractionConfidence,
    string? ExtractionStatus,
    string? ManualNote,
    RequirementTechnicalProposalSuggestedResponse Suggested,
    RequirementTechnicalProposalSelectedResponse? Selected,
    string SelectionState,
    RequirementTechnicalProposalAlternativesResponse Alternatives,
    RequirementTechnicalProposalConfidenceResponse Confidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> SystemResolutionReasons,
    IReadOnlyList<string> GlassResolutionReasons,
    IReadOnlyList<string> FinishResolutionReasons,
    bool IsTechnicallyComplete,
    bool IsPriceable,
    RequirementTechnicalProposalItemReadinessResponse Readiness,
    RequirementTechnicalProposalHistoricalEvidenceResponse HistoricalEvidence,
    RequirementTechnicalProposalTraceResponse Trace,
    IReadOnlyList<RequirementTechnicalProposalEvidenceResponse> Evidence);

public sealed record RequirementTechnicalProposalReadinessResponse(
    string State,
    bool IsReadyForConfirmation,
    bool IsReadyForPricing,
    int BlockingItems,
    int WarningItems,
    int BlockingDefinitions,
    int WarningDefinitions,
    int PricingBlockingItems,
    int PricingBlockingDefinitions,
    IReadOnlyDictionary<string, int> Categories);

public sealed record RequirementTechnicalProposalItemReadinessResponse(
    string State,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<TechnicalProposalPendingDefinitionResponse> PendingDefinitions);

public sealed record TechnicalProposalPendingDefinitionResponse(
    string Code,
    string Category,
    string Severity,
    string Field,
    string Title,
    string Message,
    string? CurrentValue,
    string RequiredAction,
    bool BlocksConfirmation,
    bool BlocksPricing,
    IReadOnlyList<string> RelatedReasonCodes);

public sealed record RequirementTechnicalProposalSuggestedResponse(
    RequirementTechnicalProposalSystemOptionResponse? System,
    RequirementTechnicalProposalGlassOptionResponse? Glass,
    RequirementTechnicalProposalFinishOptionResponse? Finish);

public sealed record RequirementTechnicalProposalSelectedResponse(
    RequirementTechnicalProposalSystemOptionResponse? System,
    RequirementTechnicalProposalGlassOptionResponse? Glass,
    RequirementTechnicalProposalFinishOptionResponse? Finish,
    DateTimeOffset SelectedAtUtc,
    Guid SelectedByUserId);

public sealed record RequirementTechnicalProposalAlternativesResponse(
    IReadOnlyList<RequirementTechnicalProposalSystemAlternativeResponse> Systems,
    IReadOnlyList<RequirementTechnicalProposalGlassAlternativeResponse> Glass,
    IReadOnlyList<RequirementTechnicalProposalFinishAlternativeResponse> Finishes);

public sealed record RequirementTechnicalProposalConfidenceResponse(
    decimal Overall,
    decimal System,
    decimal Glass,
    decimal Finish);

public sealed record RequirementTechnicalProposalSystemOptionResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string? TechnicalName,
    string? CommercialName,
    string? FunctionalType,
    string? Family,
    string? Series,
    string? CommercialLine,
    string? Variant);

public sealed record RequirementTechnicalProposalGlassOptionResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string? Family,
    string? Composition,
    string? Treatment,
    decimal? OuterThicknessMm,
    decimal? InnerThicknessMm,
    decimal? PvbThicknessMm,
    string? PvbType,
    string? PvbColor,
    decimal? ChamberThicknessMm,
    string? ProductLine,
    string? ProductToken,
    string? Pattern,
    string? Color);

public sealed record RequirementTechnicalProposalFinishOptionResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string? NormalizedType,
    string? Color,
    string? Texture,
    string? Process,
    string? CommercialCode,
    string? Material);

public sealed record RequirementTechnicalProposalSystemAlternativeResponse(
    RequirementTechnicalProposalSystemOptionResponse Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalGlassAlternativeResponse(
    RequirementTechnicalProposalGlassOptionResponse Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalFinishAlternativeResponse(
    RequirementTechnicalProposalFinishOptionResponse Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalHistoricalEvidenceResponse(
    string Status,
    int SupportCount,
    decimal? BestSimilarity,
    decimal? AverageSimilarity,
    IReadOnlyList<RequirementTechnicalProposalHistoricalExampleResponse> Examples);

public sealed record RequirementTechnicalProposalHistoricalExampleResponse(
    string CandidateId,
    string QuoteId,
    string? HistoricalReference,
    decimal SimilarityScore,
    IReadOnlyList<string> MatchedFeatures,
    IReadOnlyList<string> Differences,
    string TechnicalExplanation);

public sealed record RequirementTechnicalProposalTraceResponse(
    string? RequestedSystemRaw,
    string? RequestedProfileRaw,
    string? FunctionalType,
    string? Operation,
    string? GlassRawSpecification,
    string? GlassTypeRaw,
    string? GlassTypeNormalized,
    decimal? GlassThicknessMm,
    string? FinishRawDescription,
    string? FinishNormalizedType,
    string? FinishColorRaw,
    string? FinishColorNormalized,
    IReadOnlyList<string> SpecialFeatures,
    string? GeometryType);

public sealed record RequirementTechnicalProposalEvidenceResponse(
    int? PageNumber,
    string SourceType,
    string Text,
    string? SheetName,
    string? CellRange,
    string? SourceId,
    string? SourceFileName,
    string? ContextLabel,
    decimal? Confidence,
    string Status);
