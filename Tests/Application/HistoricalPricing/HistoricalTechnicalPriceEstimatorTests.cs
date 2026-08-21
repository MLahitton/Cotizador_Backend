using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class HistoricalTechnicalPriceEstimatorTests
{
    private static readonly HistoricalCandidateQuery Query = new(
        "PUERTA", "3831", "TEMPLADO", 6m, "CORREDIZA",
        null, null, 10m, null, 1m, 10);

    [Fact]
    public void WeightedQuantile_ReturnsWeightedMedianAndQuartiles()
    {
        HistoricalWeightedValue[] values =
            [new(100m, 1m), new(200m, 1m), new(300m, 1m), new(400m, 1m)];
        Assert.Equal(100m, HistoricalTechnicalPriceStatistics.WeightedQuantile(values, 0.25m));
        Assert.Equal(200m, HistoricalTechnicalPriceStatistics.WeightedQuantile(values, 0.50m));
        Assert.Equal(300m, HistoricalTechnicalPriceStatistics.WeightedQuantile(values, 0.75m));
    }

    [Fact]
    public async Task EstimateAsync_WithNoCandidates_IsNotPriceable()
    {
        var estimate = await Estimate([]);
        Assert.Null(estimate.Expected);
        Assert.True(estimate.RequiresReview);
        Assert.Equal(HistoricalPriceConfidenceLevel.Low, estimate.ConfidenceLevel);
    }

    [Fact]
    public async Task EstimateAsync_WithOneCandidate_ExpandsRangeAndNeverHigh()
    {
        var estimate = await Estimate([Evaluated(Candidate("one", 100m, 10m), 0.9m, "HIGH")]);
        Assert.Equal(80m, estimate.Minimum);
        Assert.Equal(100m, estimate.Expected);
        Assert.Equal(120m, estimate.Maximum);
        Assert.NotEqual(HistoricalPriceConfidenceLevel.High, estimate.ConfidenceLevel);
    }

    [Fact]
    public async Task EstimateAsync_WithMultipleCandidates_ProducesOrderedRobustRange()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("a", 90m, 10m), 0.9m, "HIGH"),
            Evaluated(Candidate("b", 100m, 10m), 0.9m, "HIGH"),
            Evaluated(Candidate("c", 110m, 10m), 0.9m, "HIGH")]);
        Assert.True(estimate.Minimum <= estimate.Expected);
        Assert.True(estimate.Expected <= estimate.Maximum);
        Assert.Equal(3, estimate.StrongComparableCount);
    }

    [Fact]
    public async Task EstimateAsync_ExcludesEconomicOutlierWithoutUsingAi2RejectedAsEconomicFilter()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("a", 100m, 10m), 0.8m, "HIGH"),
            Evaluated(Candidate("b", 105m, 10m), 0.8m, "HIGH"),
            Evaluated(Candidate("c", 110m, 10m), 0.8m, "HIGH"),
            Evaluated(Candidate("rejected", 112m, 10m), 0.1m, "REJECTED"),
            Evaluated(Candidate("outlier", 10000m, 10m), 0.8m, "HIGH")]);
        Assert.DoesNotContain("outlier", estimate.UsedComparableIds);
        Assert.Contains("rejected", estimate.UsedComparableIds);
    }

    [Fact]
    public async Task EstimateAsync_LowSimilarityDoesNotChangeEconomicWeight()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("high", 100m, 10m), 0.9m, "HIGH"),
            Evaluated(Candidate("low", 1000m, 10m), 0.2m, "LOW")]);
        Assert.Equal(100m, estimate.Expected);
        Assert.Equal(
            estimate.Comparables.Single(value => value.CandidateId == "high").FinalWeight,
            estimate.Comparables.Single(value => value.CandidateId == "low").FinalWeight);
    }

    [Fact]
    public async Task EstimateAsync_Ai2ScoreVariationDoesNotChangeEconomicPrice()
    {
        var first = await Estimate([
            Evaluated(Candidate("a", 100m, 10m), 0.85m, "HIGH"),
            Evaluated(Candidate("b", 120m, 10m), 0.35m, "LOW")]);
        var second = await Estimate([
            Evaluated(Candidate("a", 100m, 10m), 0.35m, "LOW"),
            Evaluated(Candidate("b", 120m, 10m), 0.85m, "HIGH")]);

        Assert.Equal(first.Minimum, second.Minimum);
        Assert.Equal(first.Expected, second.Expected);
        Assert.Equal(first.Maximum, second.Maximum);
    }

    [Fact]
    public async Task EstimateAsync_WithAi2Failure_FallsBackAndDegradesConfidence()
    {
        var estimate = await Estimate(
            [new HistoricalSimilarityCandidateResult(Candidate("a", 100m, 10m), null)],
            HistoricalSimilarityStatus.TechnicalFailure);
        Assert.Equal(100m, estimate.Expected);
        Assert.Equal(HistoricalPriceConfidenceLevel.Low, estimate.ConfidenceLevel);
        Assert.Contains(estimate.Assumptions, value => value.Contains("AI2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EstimateAsync_AreaMismatchIsDownweightedAndTraced()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("clean", 100m, 10m), 0.8m, "HIGH"),
            Evaluated(Candidate("mismatch", 100m, 10m, mismatch: true), 0.8m, "HIGH")]);
        Assert.True(estimate.Comparables.Single(value => value.CandidateId == "mismatch").FinalWeight
            < estimate.Comparables.Single(value => value.CandidateId == "clean").FinalWeight);
        Assert.Contains(estimate.Assumptions, value => value.Contains("HistoricalAreaMismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EstimateAsync_WithMatchingTechnicalData_CanBeStrong()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("exact", 100m, 10m), 0.9m, "HIGH")]);

        Assert.True(estimate.Comparables.Single().IsStrong);
        Assert.Equal(1, estimate.StrongComparableCount);
    }

    [Fact]
    public async Task EstimateAsync_WithDifferentKnownSystem_IsNotStrong()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("different", 100m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH")]);

        Assert.False(estimate.Comparables.Single().IsStrong);
        Assert.Equal(0, estimate.StrongComparableCount);
    }

    [Fact]
    public async Task EstimateAsync_WithMissingHistoricalSystem_IsUncertainButNotContradictory()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("missing", 100m, 10m, system: null), 0.95m, "HIGH")]);

        Assert.True(estimate.Comparables.Single().IsStrong);
        Assert.DoesNotContain("NO_EXACT_SYSTEM_COMPARABLE", estimate.MissingData);
    }

    [Fact]
    public async Task EstimateAsync_WhenAllSystemsMismatch_CapsConfidenceAndRequiresReview()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("a", 90m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH"),
            Evaluated(Candidate("b", 95m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH"),
            Evaluated(Candidate("c", 100m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH"),
            Evaluated(Candidate("d", 105m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH"),
            Evaluated(Candidate("e", 110m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH"),
            Evaluated(Candidate("f", 115m, 10m, system: "VENECIA NAPOLES"), 0.95m, "HIGH")]);

        Assert.NotEqual(HistoricalPriceConfidenceLevel.High, estimate.ConfidenceLevel);
        Assert.True(estimate.RequiresReview);
        Assert.Equal(0, estimate.StrongComparableCount);
        Assert.Contains("NO_EXACT_SYSTEM_COMPARABLE", estimate.MissingData);
    }

    [Fact]
    public async Task EstimateAsync_WithSeveralExactComparablesAndLowDispersion_CanRemainHigh()
    {
        var estimate = await Estimate([
            Evaluated(Candidate("a", 98m, 10m), 0.95m, "HIGH"),
            Evaluated(Candidate("b", 99m, 10m), 0.95m, "HIGH"),
            Evaluated(Candidate("c", 100m, 10m), 0.95m, "HIGH"),
            Evaluated(Candidate("d", 101m, 10m), 0.95m, "HIGH"),
            Evaluated(Candidate("e", 102m, 10m), 0.95m, "HIGH"),
            Evaluated(Candidate("f", 103m, 10m), 0.95m, "HIGH")]);

        Assert.Equal(HistoricalPriceConfidenceLevel.High, estimate.ConfidenceLevel);
        Assert.Equal(6, estimate.StrongComparableCount);
        Assert.False(estimate.RequiresReview);
    }

    [Fact]
    public void ResolveHistoricalUnitArea_WithTotalRowAreaAndQuantity_DividesOnce()
    {
        var candidate = Candidate("qty", 100m, 20m, quantity: 2m, width: 2m, height: 5m);
        Assert.Equal(10m, HistoricalTechnicalPriceEstimator.ResolveHistoricalUnitArea(candidate));
    }

    private static async Task<HistoricalTechnicalPriceEstimate> Estimate(
        IReadOnlyList<HistoricalSimilarityCandidateResult> candidates,
        HistoricalSimilarityStatus status = HistoricalSimilarityStatus.Completed)
    {
        var similarity = Substitute.For<IHistoricalSimilarityEvaluationService>();
        similarity.EvaluateAsync(Arg.Any<HistoricalCandidateQuery>(), Arg.Any<CancellationToken>())
            .Returns(new HistoricalSimilarityEvaluationResult(status, candidates,
                status == HistoricalSimilarityStatus.Completed ? null : "failure"));
        return await new HistoricalTechnicalPriceEstimator(similarity)
            .EstimateAsync(Query, TestContext.Current.CancellationToken);
    }

    private static HistoricalSimilarityCandidateResult Evaluated(
        HistoricalComparableCandidate candidate,
        decimal score,
        string level) =>
        new(candidate, new SimilarityCandidateResult(candidate.HistoricalItemId, score,
            level, [], [], "technical", score));

    private static HistoricalComparableCandidate Candidate(
        string id,
        decimal price,
        decimal area,
        decimal quantity = 1m,
        decimal? width = null,
        decimal? height = null,
        bool mismatch = false,
        string? system = "3831") =>
        new("quote", id, id, "description", price, price * quantity,
            "PUERTA", system, "TEMPLADO", 6m, null, "CORREDIZA",
            width, height, area, quantity, null, 100m, ["category"], [], mismatch);
}
