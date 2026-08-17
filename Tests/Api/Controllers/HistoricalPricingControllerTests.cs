using Api.Controllers;
using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class HistoricalPricingControllerTests
{
    [Fact]
    public async Task Estimate_WithValidRequestAndSimilarity_ReturnsAuditableRange()
    {
        var context = CreateContext(Estimate(similarityScore: 0.7m));
        var action = await context.Controller.Estimate(
            ValidRequest(), TestContext.Current.CancellationToken);

        var response = Assert.IsType<HistoricalTechnicalPriceEstimateResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("ESTIMATED", response.Status);
        Assert.Equal("MEDIUM", response.ConfidenceLevel);
        Assert.Equal(0.7m, Assert.Single(response.UsedComparables).Ai2SimilarityScore);
        Assert.True(response.TechnicalMinimum <= response.TechnicalExpected);
        Assert.True(response.TechnicalExpected <= response.TechnicalMaximum);
    }

    [Fact]
    public async Task Estimate_WithAi2TechnicalFailure_ReturnsFallbackAssumption()
    {
        var estimate = Estimate(similarityScore: null) with
        {
            ConfidenceScore = 0.39m,
            ConfidenceLevel = HistoricalPriceConfidenceLevel.Low,
            Assumptions = ["Similarity AI2 no disponible; fallback Backend."]
        };
        var action = await CreateContext(estimate).Controller.Estimate(
            ValidRequest(), TestContext.Current.CancellationToken);
        var response = Assert.IsType<HistoricalTechnicalPriceEstimateResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.True(response.RequiresReview);
        Assert.Contains(response.Assumptions, value => value.Contains("fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Estimate_WithZeroComparables_ReturnsNotPriceable()
    {
        var estimate = new HistoricalTechnicalPriceEstimate(
            "COP", null, null, null, 0m, HistoricalPriceConfidenceLevel.Low,
            "HISTORICAL_COMPARABLES", 0, 0, 0, true,
            ["No existen comparables economicos utilizables."], [], [], []);
        var action = await CreateContext(estimate).Controller.Estimate(
            ValidRequest(), TestContext.Current.CancellationToken);
        var response = Assert.IsType<HistoricalTechnicalPriceEstimateResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("NOT_PRICEABLE", response.Status);
        Assert.Null(response.TechnicalExpected);
    }

    [Fact]
    public async Task Estimate_WithUnavailableCorpus_ReturnsControlledServiceUnavailable()
    {
        var context = CreateContext(Estimate(), corpusAvailable: false);
        var action = await context.Controller.Estimate(
            ValidRequest(), TestContext.Current.CancellationToken);
        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.IsType<ProblemDetails>(result.Value);
        await context.Corpus.Received(1).ReloadAsync(
            TestContext.Current.CancellationToken);
        await context.Estimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("", 6, 9.35, 1)]
    [InlineData("PUERTA", 0, 9.35, 1)]
    [InlineData("PUERTA", 6, 0, 1)]
    [InlineData("PUERTA", 6, 9.35, 0)]
    public async Task Estimate_WithInvalidInput_ReturnsBadRequest(
        string category,
        decimal thickness,
        decimal area,
        decimal quantity)
    {
        var context = CreateContext(Estimate());
        var request = ValidRequest() with
        {
            Category = category,
            GlassThickness = thickness,
            AreaM2 = area,
            Quantity = quantity
        };
        var action = await context.Controller.Estimate(
            request, TestContext.Current.CancellationToken);
        var result = Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.IsType<ProblemDetails>(result.Value);
    }

    private static TestContextData CreateContext(
        HistoricalTechnicalPriceEstimate estimate,
        bool corpusAvailable = true)
    {
        var estimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();
        estimator.EstimateAsync(Arg.Any<HistoricalCandidateQuery>(), Arg.Any<CancellationToken>())
            .Returns(estimate);
        var corpus = Substitute.For<IHistoricalQuoteCorpus>();
        corpus.Current.Returns(corpusAvailable
            ? new HistoricalCorpusSnapshot(true, "path", DateTimeOffset.UtcNow, [], [])
            : HistoricalCorpusSnapshot.Unavailable(null));
        corpus.ReloadAsync(Arg.Any<CancellationToken>())
            .Returns(HistoricalCorpusSnapshot.Unavailable(null));
        return new TestContextData(
            new HistoricalPricingController(estimator, corpus), estimator, corpus);
    }

    private static HistoricalTechnicalPriceEstimate Estimate(decimal? similarityScore = 0.7m)
    {
        var comparable = new HistoricalTechnicalPriceComparable(
            "candidate", "quote", "04", 5_000_000m, 0.78m,
            similarityScore, similarityScore is null ? null : "MEDIUM",
            0.55m, 9.35m, 5_100_000m, false, false);
        return new HistoricalTechnicalPriceEstimate(
            "COP", 5_000_000m, 5_100_000m, 5_500_000m,
            0.59m, HistoricalPriceConfidenceLevel.Medium,
            "HISTORICAL_COMPARABLES", 1,
            similarityScore is null ? 0 : 1, 0, true, [], [],
            [comparable.CandidateId], [comparable]);
    }

    private static HistoricalTechnicalPriceEstimateRequest ValidRequest() =>
        new("PV-01", "PUERTA", "3831", "TEMPLADO", 6m, null,
            "CORREDIZA", null, null, 9.35m, 1m, null);

    private sealed record TestContextData(
        HistoricalPricingController Controller,
        IHistoricalTechnicalPriceEstimator Estimator,
        IHistoricalQuoteCorpus Corpus);
}
