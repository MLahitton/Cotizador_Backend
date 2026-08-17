namespace Application.Common.Abstractions.HistoricalPricing;

public sealed record HistoricalCommercialPriceEstimate(
    string Currency,
    string PricingSource,
    HistoricalPricingBasis PricingBasis,
    decimal? TechnicalMinimum,
    decimal? TechnicalExpected,
    decimal? TechnicalMaximum,
    decimal? AdministrationMinimum,
    decimal? AdministrationExpected,
    decimal? AdministrationMaximum,
    decimal? ContingencyMinimum,
    decimal? ContingencyExpected,
    decimal? ContingencyMaximum,
    decimal? ProfitMinimum,
    decimal? ProfitExpected,
    decimal? ProfitMaximum,
    decimal? VatOnProfitMinimum,
    decimal? VatOnProfitExpected,
    decimal? VatOnProfitMaximum,
    decimal? FinalMinimum,
    decimal? FinalExpected,
    decimal? FinalMaximum,
    decimal ConfidenceScore,
    HistoricalPriceConfidenceLevel ConfidenceLevel,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData)
{
    public decimal? UnitMinimum => FinalMinimum;
    public decimal? UnitExpected => FinalExpected;
    public decimal? UnitMaximum => FinalMaximum;
}

public interface IHistoricalCommercialPriceEstimator
{
    Task<HistoricalCommercialPriceEstimate> EstimateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default);

    HistoricalCommercialPriceEstimate FromTechnical(
        HistoricalTechnicalPriceEstimate technicalEstimate);
}
