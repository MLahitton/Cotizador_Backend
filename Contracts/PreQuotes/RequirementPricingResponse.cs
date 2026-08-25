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
    IReadOnlyList<RequirementPricingItemResponse> Items);

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
    IReadOnlyList<RequirementPricingComparableResponse> Comparables);

public sealed record RequirementPricingComparableResponse(
    string CandidateId,
    string? HistoricalReference,
    decimal PublicUnitPrice,
    decimal ProjectedPrice,
    decimal BackendScore,
    decimal? Ai2Similarity,
    string? SimilarityLevel,
    decimal FinalWeight);
