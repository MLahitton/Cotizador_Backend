using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class HistoricalCommercialPriceEstimatorTests
{
    [Fact]
    public async Task EstimateAsync_WithPublicQuotedPrices_DoesNotApplyCommercialRatesAgain()
    {
        var result = await Estimate(new HistoricalTechnicalPriceEstimate(
            "COP", 100m, 200m, 300m, 0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            "HISTORICAL_COMPARABLES", 4, 4, 0, true,
            ["technical-assumption"], ["technical-missing"], [], []));

        Assert.Equal(HistoricalPricingBasis.PublicQuotedItemPrices, result.PricingBasis);
        Assert.Equal(0m, result.AdministrationMinimum);
        Assert.Equal(0m, result.AdministrationExpected);
        Assert.Equal(0m, result.AdministrationMaximum);
        Assert.Equal(0m, result.ContingencyMinimum);
        Assert.Equal(0m, result.ContingencyExpected);
        Assert.Equal(0m, result.ContingencyMaximum);
        Assert.Equal(0m, result.ProfitMinimum);
        Assert.Equal(0m, result.ProfitExpected);
        Assert.Equal(0m, result.ProfitMaximum);
        Assert.Equal(0m, result.VatOnProfitMinimum);
        Assert.Equal(0m, result.VatOnProfitExpected);
        Assert.Equal(0m, result.VatOnProfitMaximum);
        Assert.Equal(100m, result.FinalMinimum);
        Assert.Equal(200m, result.FinalExpected);
        Assert.Equal(300m, result.FinalMaximum);
    }

    [Fact]
    public async Task EstimateAsync_PreservesTechnicalConfidenceReviewAndTraceability()
    {
        var result = await Estimate(new HistoricalTechnicalPriceEstimate(
            "COP", 100m, 200m, 300m, 0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            "HISTORICAL_COMPARABLES", 4, 4, 0, true,
            ["technical-assumption"], ["technical-missing"], [], []));

        Assert.Equal(0.59m, result.ConfidenceScore);
        Assert.Equal(HistoricalPriceConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.True(result.RequiresReview);
        Assert.Contains("technical-assumption", result.Assumptions);
        Assert.Contains(HistoricalCommercialPricingRules.TransportAssumption, result.Assumptions);
        Assert.Contains("technical-missing", result.MissingData);
        Assert.Contains(HistoricalCommercialPricingRules.TransportMissingData, result.MissingData);
    }

    [Fact]
    public async Task EstimateAsync_WithNotPriceableTechnicalRange_KeepsCommercialRangeNull()
    {
        var result = await Estimate(new HistoricalTechnicalPriceEstimate(
            "COP", null, null, null, 0m,
            HistoricalPriceConfidenceLevel.Low,
            "HISTORICAL_COMPARABLES", 0, 0, 0, true,
            [], [], [], []));

        Assert.Null(result.FinalMinimum);
        Assert.Null(result.FinalExpected);
        Assert.Null(result.FinalMaximum);
    }

    [Fact]
    public void PricingRules_RemainAvailableForFutureInternalCostBasis()
    {
        Assert.Equal(0.04m, HistoricalCommercialPricingRules.AdministrationRate);
        Assert.Equal(0.01m, HistoricalCommercialPricingRules.ContingencyRate);
        Assert.Equal(0.05m, HistoricalCommercialPricingRules.ProfitRate);
        Assert.Equal(0.19m, HistoricalCommercialPricingRules.VatOnProfitRate);
        Assert.NotEqual(
            HistoricalPricingBasis.PublicQuotedItemPrices,
            HistoricalPricingBasis.InternalCostBasis);
    }

    [Fact]
    public async Task EstimateAsync_WithInternalCostBasis_IsExplicitlyNotImplemented()
    {
        var technical = new HistoricalTechnicalPriceEstimate(
            "COP", 100m, 200m, 300m, 0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            "INTERNAL_COSTS", 4, 4, 0, true, [], [], [], [])
        {
            PricingBasis = HistoricalPricingBasis.InternalCostBasis
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => Estimate(technical));
    }

    private static async Task<HistoricalCommercialPriceEstimate> Estimate(
        HistoricalTechnicalPriceEstimate technical)
    {
        var estimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();
        estimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(technical);
        return await new HistoricalCommercialPriceEstimator(estimator).EstimateAsync(
            new HistoricalCandidateQuery(
                "PUERTA", "3831", "TEMPLADO", 6m, "CORREDIZA",
                null, null, 9.35m, null, 1m),
            TestContext.Current.CancellationToken);
    }
}
