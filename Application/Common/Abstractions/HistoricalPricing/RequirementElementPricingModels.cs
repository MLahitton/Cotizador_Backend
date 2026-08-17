using Application.Common.Abstractions.DocumentProcessing;

namespace Application.Common.Abstractions.HistoricalPricing;

public sealed record RequirementElementHistoricalPricingMapping(
    int ElementId,
    string? Reference,
    HistoricalCandidateQuery CandidateQuery,
    CanonicalExtractionValueStatus ExtractionStatus,
    decimal? ExtractionConfidence,
    IReadOnlyList<string> MappingWarnings,
    bool RequiresReview);

public sealed record PricedRequirementElement(
    int ElementId,
    string? Reference,
    HistoricalCandidateQuery CandidateQuery,
    HistoricalTechnicalPriceEstimate TechnicalEstimate,
    HistoricalCommercialPriceEstimate CommercialEstimate,
    IReadOnlyList<string> MappingWarnings,
    bool RequiresReview)
{
    public decimal? Quantity => CandidateQuery.Quantity;
    public decimal? UnitMinimum => CommercialEstimate.UnitMinimum;
    public decimal? UnitExpected => CommercialEstimate.UnitExpected;
    public decimal? UnitMaximum => CommercialEstimate.UnitMaximum;
    public decimal? LineMinimum => RequirementElementLinePrice.Calculate(
        UnitMinimum, Quantity);
    public decimal? LineExpected => RequirementElementLinePrice.Calculate(
        UnitExpected, Quantity);
    public decimal? LineMaximum => RequirementElementLinePrice.Calculate(
        UnitMaximum, Quantity);
}

public interface IRequirementElementToHistoricalPricingMapper
{
    RequirementElementHistoricalPricingMapping Map(
        StructuredItemData item,
        IReadOnlyList<ProcessingWarningData> warnings);
}

public interface IPriceRequirementElementService
{
    Task<PricedRequirementElement> PriceAsync(
        StructuredItemData item,
        IReadOnlyList<ProcessingWarningData> warnings,
        CancellationToken cancellationToken = default);
}

public enum RequirementElementPricingStatus
{
    Priceable = 1,
    NotPriceable = 2,
    TechnicalFailure = 3
}

public sealed record PricedRequirementExtractionItem(
    int ElementId,
    string? Reference,
    RequirementElementPricingStatus Status,
    HistoricalCandidateQuery? CandidateQuery,
    HistoricalTechnicalPriceEstimate? TechnicalEstimate,
    HistoricalCommercialPriceEstimate? CommercialEstimate,
    IReadOnlyList<string> MappingWarnings,
    bool RequiresReview,
    string? FailureCode = null,
    string? FailureMessage = null)
{
    public decimal? Quantity => CandidateQuery?.Quantity;
    public decimal? UnitMinimum => CommercialEstimate?.UnitMinimum;
    public decimal? UnitExpected => CommercialEstimate?.UnitExpected;
    public decimal? UnitMaximum => CommercialEstimate?.UnitMaximum;
    public decimal? LineMinimum => RequirementElementLinePrice.Calculate(
        UnitMinimum, Quantity);
    public decimal? LineExpected => RequirementElementLinePrice.Calculate(
        UnitExpected, Quantity);
    public decimal? LineMaximum => RequirementElementLinePrice.Calculate(
        UnitMaximum, Quantity);
}

internal static class RequirementElementLinePrice
{
    public static decimal? Calculate(decimal? unitPrice, decimal? quantity) =>
        unitPrice is not null && quantity is > 0
            ? unitPrice.Value * quantity.Value
            : null;
}

public sealed record PricedRequirementExtraction(
    int ItemCount,
    int PricedItemCount,
    int NotPriceableItemCount,
    int RequiresReviewItemCount,
    decimal? CommercialMinimum,
    decimal? CommercialExpected,
    decimal? CommercialMaximum,
    string? Currency,
    decimal ConfidenceScore,
    HistoricalPriceConfidenceLevel ConfidenceLevel,
    bool IsPartial,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PricedRequirementExtractionItem> Items);

public interface IPriceRequirementExtractionService
{
    Task<PricedRequirementExtraction> PriceAsync(
        IReadOnlyList<StructuredItemData> items,
        IReadOnlyList<ProcessingWarningData> warnings,
        CancellationToken cancellationToken = default);
}
