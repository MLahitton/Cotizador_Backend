using Api.Controllers;
using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class HistoricalCommercialPricingControllerTests
{
    [Fact]
    public async Task Estimate_WithValidRequest_ReturnsCommercialRange()
    {
        var estimator = Substitute.For<IHistoricalCommercialPriceEstimator>();
        estimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalCommercialPriceEstimate(
                "COP", "HISTORICAL_PUBLIC_QUOTED_ITEM_PRICES",
                HistoricalPricingBasis.PublicQuotedItemPrices,
                100m, 200m, 300m,
                0m, 0m, 0m,
                0m, 0m, 0m,
                0m, 0m, 0m,
                0m, 0m, 0m,
                100m, 200m, 300m,
                0.59m, HistoricalPriceConfidenceLevel.Medium,
                true, ["TRANSPORT_NOT_INCLUDED"], ["TRANSPORT_NOT_CONFIRMED"]));
        var corpus = Substitute.For<IHistoricalQuoteCorpus>();
        corpus.Current.Returns(new HistoricalCorpusSnapshot(
            true, "path", DateTimeOffset.UtcNow, [], []));
        var controller = new HistoricalCommercialPricingController(estimator, corpus);

        var action = await controller.Estimate(
            new HistoricalTechnicalPriceEstimateRequest(
                "PV-06", "PUERTA", "3831", "TEMPLADO", 6m, null,
                "CORREDIZA", 3740m, 2500m, 9.35m, 1m, null),
            TestContext.Current.CancellationToken);

        var response = Assert.IsType<HistoricalCommercialPriceEstimateResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("ESTIMATED", response.Status);
        Assert.Equal(200m, response.FinalExpected);
        Assert.Equal("PUBLIC_QUOTED_ITEM_PRICES", response.PricingBasis);
        Assert.Equal("MEDIUM", response.ConfidenceLevel);
        Assert.True(response.RequiresReview);
    }
}
