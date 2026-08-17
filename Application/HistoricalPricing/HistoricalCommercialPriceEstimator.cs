using Application.Common.Abstractions.HistoricalPricing;

namespace Application.HistoricalPricing;

public sealed class HistoricalCommercialPriceEstimator(
    IHistoricalTechnicalPriceEstimator technicalEstimator)
    : IHistoricalCommercialPriceEstimator
{
    public async Task<HistoricalCommercialPriceEstimate> EstimateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var technical = await technicalEstimator.EstimateAsync(query, cancellationToken);
        return FromTechnical(technical);
    }

    public HistoricalCommercialPriceEstimate FromTechnical(
        HistoricalTechnicalPriceEstimate technical)
    {
        var minimum = Components(technical.Minimum, technical.PricingBasis);
        var expected = Components(technical.Expected, technical.PricingBasis);
        var maximum = Components(technical.Maximum, technical.PricingBasis);
        return new HistoricalCommercialPriceEstimate(
            technical.Currency,
            "HISTORICAL_PUBLIC_QUOTED_ITEM_PRICES",
            technical.PricingBasis,
            technical.Minimum,
            technical.Expected,
            technical.Maximum,
            minimum.Administration,
            expected.Administration,
            maximum.Administration,
            minimum.Contingency,
            expected.Contingency,
            maximum.Contingency,
            minimum.Profit,
            expected.Profit,
            maximum.Profit,
            minimum.VatOnProfit,
            expected.VatOnProfit,
            maximum.VatOnProfit,
            minimum.Final,
            expected.Final,
            maximum.Final,
            technical.ConfidenceScore,
            technical.ConfidenceLevel,
            technical.RequiresReview,
            technical.Assumptions
                .Append(HistoricalCommercialPricingRules.TransportAssumption)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            technical.MissingData
                .Append(HistoricalCommercialPricingRules.TransportMissingData)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static CommercialComponents Components(
        decimal? technical,
        HistoricalPricingBasis pricingBasis)
    {
        if (technical is null)
        {
            return new CommercialComponents(null, null, null, null, null);
        }

        return pricingBasis switch
        {
            HistoricalPricingBasis.PublicQuotedItemPrices =>
                new CommercialComponents(0m, 0m, 0m, 0m, technical),
            HistoricalPricingBasis.InternalCostBasis =>
                throw new NotSupportedException(
                    "INTERNAL_COST_BASIS no esta implementado."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pricingBasis), pricingBasis, null)
        };
    }

    private sealed record CommercialComponents(
        decimal? Administration,
        decimal? Contingency,
        decimal? Profit,
        decimal? VatOnProfit,
        decimal? Final);
}

public static class HistoricalCommercialPricingRules
{
    public const decimal AdministrationRate = 0.04m;
    public const decimal ContingencyRate = 0.01m;
    public const decimal ProfitRate = 0.05m;
    public const decimal VatOnProfitRate = 0.19m;
    public const string TransportAssumption = "TRANSPORT_NOT_INCLUDED";
    public const string TransportMissingData = "TRANSPORT_NOT_CONFIRMED";
}
