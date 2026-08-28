using System.ComponentModel.DataAnnotations;

namespace Contracts.HistoricalPricing;

public sealed record HistoricalTechnicalPriceEstimateRequest(
    string? Reference,
    [Required, MinLength(1)] string Category,
    [Required, MinLength(1)] string System,
    [Required, MinLength(1)] string GlassFamily,
    [Range(typeof(decimal), "0.0001", "1000",
        ParseLimitsInInvariantCulture = true)] decimal GlassThickness,
    string? GlassComposition,
    [Required, MinLength(1)] string Configuration,
    [Range(typeof(decimal), "0.0001", "1000000",
        ParseLimitsInInvariantCulture = true)] decimal? WidthMm,
    [Range(typeof(decimal), "0.0001", "1000000",
        ParseLimitsInInvariantCulture = true)] decimal? HeightMm,
    [Range(typeof(decimal), "0.0001", "1000000",
        ParseLimitsInInvariantCulture = true)] decimal AreaM2,
    [Range(typeof(decimal), "0.0001", "1000000",
        ParseLimitsInInvariantCulture = true)] decimal Quantity,
    string? Finish,
    IReadOnlyList<string>? ExcludeCandidateIds = null,
    IReadOnlyList<string>? ExcludeQuoteIds = null);

public sealed record HistoricalTechnicalPriceComparableResponse(
    string CandidateId,
    string? HistoricalReference,
    decimal BackendScore,
    decimal? Ai2SimilarityScore,
    string? SimilarityLevel,
    decimal FinalWeight,
    decimal PublicUnitPrice,
    decimal HistoricalUnitArea,
    decimal ProjectedPrice,
    string MatchingTier,
    bool MatchedSystem,
    bool MatchedGlass,
    bool MatchedFinish,
    bool MatchedCommercialLine,
    IReadOnlyList<string> FallbackReasons);

public sealed record HistoricalTechnicalPriceEstimateResponse(
    string Status,
    string Currency,
    string PricingSource,
    decimal? TechnicalMinimum,
    decimal? TechnicalExpected,
    decimal? TechnicalMaximum,
    decimal ConfidenceScore,
    string ConfidenceLevel,
    int CandidateCount,
    int SimilarityEvaluatedCount,
    int StrongComparableCount,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<HistoricalTechnicalPriceComparableResponse> UsedComparables);
