namespace Contracts.PreQuotes;

public sealed record RequirementPricingResponse(
    Guid RequirementId,
    Guid TechnicalProposalId,
    string Currency,
    string PricingBasis,
    int ItemCount,
    int PricedItemCount,
    int NotPriceableItemCount,
    int ItemsRequiringReview,
    RequirementPricingRangeResponse EstimatedSubtotal,
    bool IsCompleteTotal,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<RequirementPricingItemResponse> Items,
    decimal? OriginalGrandTotal,
    decimal? CurrentGrandTotal,
    decimal? DeltaGrandTotal);

public sealed record RequirementPricingRangeResponse(
    decimal? Minimum,
    decimal? Expected,
    decimal? Maximum);

public sealed record RequirementPricingItemResponse(
    Guid ProposalItemId,
    Guid ExtractedItemId,
    string? ElementId,
    int Sequence,
    string? Reference,
    string Description,
    string Status,
    string ConfigurationSource,
    decimal? Quantity,
    decimal? PricingAreaM2,
    RequirementPricingRangeResponse Unit,
    RequirementPricingRangeResponse Line,
    decimal? ConfidenceScore,
    string? ConfidenceLevel,
    bool RequiresReview,
    IReadOnlyList<string> MappingWarnings,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<RequirementPricingComparableResponse> Comparables,
    RequirementPricingRangeResponse? OriginalUnit,
    RequirementPricingRangeResponse? CurrentUnit,
    RequirementPricingRangeResponse? DeltaUnit,
    RequirementPricingRangeResponse? OriginalLine,
    RequirementPricingRangeResponse? CurrentLine,
    RequirementPricingRangeResponse? DeltaLine,
    string? PriceSource,
    string? RepriceAttemptState,
    string? RepriceAttemptReason);

public sealed record RequirementPricingComparableResponse(
    string CandidateId,
    string? HistoricalReference,
    decimal PublicUnitPrice,
    decimal ProjectedPrice,
    decimal BackendScore,
    decimal? Ai2Similarity,
    string? SimilarityLevel,
    decimal FinalWeight,
    string MatchingTier,
    bool MatchedSystem,
    bool MatchedGlass,
    bool MatchedFinish,
    bool MatchedCommercialLine,
    IReadOnlyList<string> FallbackReasons);

public sealed record RepriceRequirementPricingItemRequest(
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId,
    int? Quantity = null,
    int? WidthMm = null,
    int? HeightMm = null);

public sealed record RepriceRequirementPricingItemResponse(
    Guid RequirementId,
    Guid TechnicalProposalId,
    Guid TechnicalProposalItemId,
    RequirementPricingItemConfigurationResponse Configuration,
    RepriceRequirementPricingItemPriceResponse Pricing,
    RepriceRequirementPricingSummaryResponse Summary,
    IReadOnlyList<RequirementPricingComparableResponse> Comparables);

public sealed record RequirementPricingItemConfigurationResponse(
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId);

public sealed record RepriceRequirementPricingItemPriceResponse(
    decimal? OriginalUnitPrice,
    decimal? CurrentUnitPrice,
    decimal? DeltaUnitPrice,
    decimal? OriginalLineTotal,
    decimal? CurrentLineTotal,
    decimal? DeltaLineTotal,
    string State,
    string? PriceSource,
    string? RepriceAttemptState,
    string? RepriceAttemptReason);

public sealed record RepriceRequirementPricingSummaryResponse(
    decimal? OriginalGrandTotal,
    decimal? CurrentGrandTotal,
    decimal? DeltaGrandTotal);
