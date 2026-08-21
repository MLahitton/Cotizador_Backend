using System.Text.Json;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class EvaluateHistoricalSimilarityServiceTests
{
    private static readonly HistoricalCandidateQuery Query = new(
        "VENTANA", "8025", "TEMPLADO", 6m, "CORREDIZA",
        3090m, 1900m, 5.87m, "NEGRO", 1m, 10);

    [Fact]
    public async Task EvaluateAsync_MapsCompleteTechnicalRequestWithoutPricesAndPreservesAiOrder()
    {
        var candidates = CreateCandidates();
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Query).Returns(candidates);
        var client = Substitute.For<IAi2SimilarityClient>();
        SimilarityEvaluationRequest? captured = null;
        client.EvaluateAsync(Arg.Do<SimilarityEvaluationRequest>(value => captured = value), Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityClientResult.Succeeded(new SimilarityEvaluationResult([
                Similarity("item-2", 0.95m), Similarity("item-1", 0.90m)])));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.Completed, result.Status);
        Assert.Equal(["item-2", "item-1"], result.Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.NotNull(captured);
        Assert.Equal("8025", captured.Element.System);
        var mapped = Assert.Single(captured.Candidates, value => value.CandidateId == "item-1");
        Assert.Equal("quote-1", mapped.QuoteId);
        Assert.Equal("V-01", mapped.Reference);
        Assert.Equal("VENTANA", mapped.Category);
        Assert.Equal("8025", mapped.System);
        Assert.Equal("TEMPLADO", mapped.GlassFamily);
        Assert.Equal(6m, mapped.GlassThickness);
        Assert.Equal("MONOLITICO", mapped.GlassComposition);
        Assert.Equal(3090m, mapped.WidthMm);
        Assert.Equal(1900m, mapped.HeightMm);
        Assert.Equal(5.87m, mapped.AreaM2);
        Assert.Equal(1m, mapped.Quantity);
        Assert.Equal(90m / HistoricalCandidateRankingWeights.MaximumScore,
            mapped.BackendPreliminaryScore);
        Assert.Equal(["category", "glass"], mapped.MatchedSignals);
        var json = JsonSerializer.Serialize(captured);
        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public_total", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task EvaluateAsync_WithInvalidCorrelation_PreservesBackendShortlist(string scenario)
    {
        var candidates = CreateCandidates();
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Query).Returns(candidates);
        var returned = scenario switch
        {
            "unknown" => new[] { Similarity("item-1", 0.9m), Similarity("other", 0.8m) },
            "missing" => new[] { Similarity("item-1", 0.9m) },
            _ => new[] { Similarity("item-1", 0.9m), Similarity("item-1", 0.8m) }
        };
        var client = Substitute.For<IAi2SimilarityClient>();
        client.EvaluateAsync(Arg.Any<SimilarityEvaluationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityClientResult.Succeeded(new SimilarityEvaluationResult(returned)));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, result.Status);
        Assert.Equal("AI2_SIMILARITY_INVALID_CORRELATION", result.FailureCode);
        Assert.Equal(["item-1", "item-2"], result.Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.All(result.Candidates, value => Assert.Null(value.Similarity));
    }

    [Fact]
    public async Task EvaluateAsync_WhenAi2Fails_PreservesBackendShortlistWithoutInventingSimilarity()
    {
        var candidates = CreateCandidates();
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Query).Returns(candidates);
        var client = Substitute.For<IAi2SimilarityClient>();
        client.EvaluateAsync(Arg.Any<SimilarityEvaluationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_TRANSPORT_ERROR"));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, result.Status);
        Assert.Equal("AI2_SIMILARITY_TRANSPORT_ERROR", result.FailureCode);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, value => Assert.Null(value.Similarity));
    }

    [Fact]
    public async Task EvaluateAsync_WhenCorpusIsNotLoaded_ReloadsBeforeFindingCandidates()
    {
        var corpus = UnloadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Query).Returns([]);
        var client = Substitute.For<IAi2SimilarityClient>();

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.Completed, result.Status);
        await corpus.Received(1).ReloadAsync(TestContext.Current.CancellationToken);
        candidateService.Received(1).Find(Query);
        await client.DidNotReceive().EvaluateAsync(
            Arg.Any<SimilarityEvaluationRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static HistoricalComparableCandidate[] CreateCandidates() =>
    [
        new("quote-1", "item-1", "V-01", "Ventana corrediza", 100m, 100m,
            "VENTANA", "8025", "TEMPLADO", 6m, "MONOLITICO", "CORREDIZA",
            3090m, 1900m, 5.87m, 1m, "NEGRO", 90m,
            ["category", "glass"], ["system"], false),
        new("quote-2", "item-2", "V-02", "Ventana alterna", 200m, 200m,
            "VENTANA", "SERIE 70", "TEMPLADO", 6m, "MONOLITICO", "CORREDIZA",
            3000m, 1800m, 5.4m, 1m, "NEGRO", 80m,
            ["category", "glass"], ["system"], false)
    ];

    private static SimilarityCandidateResult Similarity(string id, decimal score) =>
        new(id, score, "HIGH", ["glass"], ["system"], "Comparacion tecnica.", 0.9m);

    private static IHistoricalQuoteCorpus LoadedCorpus()
    {
        var corpus = Substitute.For<IHistoricalQuoteCorpus>();
        corpus.Current.Returns(new HistoricalCorpusSnapshot(
            true,
            "test-corpus",
            DateTimeOffset.UtcNow,
            [],
            []));
        return corpus;
    }

    private static IHistoricalQuoteCorpus UnloadedCorpus()
    {
        var corpus = Substitute.For<IHistoricalQuoteCorpus>();
        corpus.Current.Returns(HistoricalCorpusSnapshot.Unavailable("test-corpus"));
        corpus.ReloadAsync(Arg.Any<CancellationToken>())
            .Returns(new HistoricalCorpusSnapshot(
                true,
                "test-corpus",
                DateTimeOffset.UtcNow,
                [],
                []));
        return corpus;
    }
}
