namespace Application.Common.Abstractions.HistoricalPricing;

public enum HistoricalPricingBasis
{
    PublicQuotedItemPrices = 1,
    InternalCostBasis = 2
}

public enum HistoricalPriceConfidenceLevel
{
    Low = 1,
    Medium = 2,
    Good = 3,
    High = 4
}

public sealed record HistoricalTechnicalPriceComparable(
    string CandidateId,
    string HistoricalQuoteId,
    string? HistoricalReference,
    decimal PublicUnitPrice,
    decimal BackendTechnicalScore,
    decimal? Ai2SimilarityScore,
    string? SimilarityLevel,
    decimal FinalWeight,
    decimal HistoricalUnitArea,
    decimal ProjectedPrice,
    bool IsStrong,
    bool HasAreaMismatch,
    string MatchingTier = "UNSPECIFIED",
    bool MatchedSystem = false,
    bool MatchedGlass = false,
    bool MatchedFinish = false,
    bool MatchedCommercialLine = false,
    IReadOnlyList<string>? FallbackReasons = null);

public sealed record HistoricalTechnicalPriceEstimate(
    string Currency,
    decimal? Minimum,
    decimal? Expected,
    decimal? Maximum,
    decimal ConfidenceScore,
    HistoricalPriceConfidenceLevel ConfidenceLevel,
    string PricingSource,
    int CandidateCount,
    int SimilarityEvaluatedCount,
    int StrongComparableCount,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<string> UsedComparableIds,
    IReadOnlyList<HistoricalTechnicalPriceComparable> Comparables)
{
    public HistoricalPricingBasis PricingBasis { get; init; } =
        HistoricalPricingBasis.PublicQuotedItemPrices;

    public decimal? UnitMinimum => Minimum;
    public decimal? UnitExpected => Expected;
    public decimal? UnitMaximum => Maximum;
}

public interface IHistoricalTechnicalPriceEstimator
{
    Task<HistoricalTechnicalPriceEstimate> EstimateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default);
}
