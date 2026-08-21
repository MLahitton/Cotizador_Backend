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
        SimilarityBatchEvaluationRequest? captured = null;
        client.EvaluateBatchAsync(
                Arg.Do<SimilarityBatchEvaluationRequest>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult([
                    BatchResult(
                        "single",
                        [Similarity("item-2", 0.95m), Similarity("item-1", 0.90m)])
                ])));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.Completed, result.Status);
        Assert.Equal(["item-2", "item-1"], result.Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.NotNull(captured);
        var request = Assert.Single(captured.Requests);
        Assert.Equal("single", request.RequestId);
        Assert.Equal("8025", request.Element.System);
        var mapped = Assert.Single(request.Candidates, value => value.CandidateId == "item-1");
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
        client.EvaluateBatchAsync(
                Arg.Any<SimilarityBatchEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult([
                    BatchResult("single", returned)
                ])));

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
        client.EvaluateBatchAsync(
                Arg.Any<SimilarityBatchEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Failed(
                "AI2_SIMILARITY_TRANSPORT_ERROR"));

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
        await client.DidNotReceive().EvaluateBatchAsync(
            Arg.Any<SimilarityBatchEvaluationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateBatchAsync_SendsMultipleQueriesInSingleBatchWithoutPrices()
    {
        var candidates = CreateCandidates();
        var query2 = Query with { System = "3831", Area = 9.35m };
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Query).Returns(candidates);
        candidateService.Find(query2).Returns(candidates);
        var client = Substitute.For<IAi2SimilarityClient>();
        SimilarityBatchEvaluationRequest? captured = null;
        client.EvaluateBatchAsync(
                Arg.Do<SimilarityBatchEvaluationRequest>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult([
                    BatchResult("item-a", [
                        Similarity("item-1", 0.90m),
                        Similarity("item-2", 0.80m)
                    ]),
                    BatchResult("item-b", [
                        Similarity("item-2", 0.95m),
                        Similarity("item-1", 0.85m)
                    ])
                ])));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateBatchAsync(
                [
                    new HistoricalSimilarityBatchQuery("item-a", Query),
                    new HistoricalSimilarityBatchQuery("item-b", query2)
                ],
                TestContext.Current.CancellationToken);

        Assert.Equal(["item-a", "item-b"], result.Keys);
        Assert.Equal(["item-1", "item-2"],
            result["item-a"].Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.Equal(["item-2", "item-1"],
            result["item-b"].Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.NotNull(captured);
        Assert.Equal(2, captured.Requests.Count);
        Assert.Equal(["item-a", "item-b"], captured.Requests.Select(value => value.RequestId));
        var json = JsonSerializer.Serialize(captured);
        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public_total", json, StringComparison.OrdinalIgnoreCase);
        await client.DidNotReceive().EvaluateAsync(
            Arg.Any<SimilarityEvaluationRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateBatchAsync_WithPerItemFailure_PreservesBackendShortlist()
    {
        var candidates = CreateCandidates();
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Arg.Any<HistoricalCandidateQuery>()).Returns(candidates);
        var client = Substitute.For<IAi2SimilarityClient>();
        client.EvaluateBatchAsync(
                Arg.Any<SimilarityBatchEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult([
                    BatchResult("item-a", [
                        Similarity("item-1", 0.90m),
                        Similarity("item-2", 0.80m)
                    ]),
                    new SimilarityBatchResultItem(
                        "item-b",
                        "FAILED",
                        [],
                        "AI2_SIMILARITY_ITEM_FAILED")
                ])));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateBatchAsync(
                [
                    new HistoricalSimilarityBatchQuery("item-a", Query),
                    new HistoricalSimilarityBatchQuery("item-b", Query)
                ],
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.Completed, result["item-a"].Status);
        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, result["item-b"].Status);
        Assert.Equal("AI2_SIMILARITY_ITEM_FAILED", result["item-b"].FailureCode);
        Assert.Equal(["item-1", "item-2"],
            result["item-b"].Candidates.Select(value => value.Candidate.HistoricalItemId));
        Assert.All(result["item-b"].Candidates, value => Assert.Null(value.Similarity));
    }

    [Fact]
    public async Task EvaluateBatchAsync_WithCandidateLeakage_FailsOnlyAffectedRequest()
    {
        var candidates = CreateCandidates();
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Arg.Any<HistoricalCandidateQuery>()).Returns(candidates);
        var client = Substitute.For<IAi2SimilarityClient>();
        client.EvaluateBatchAsync(
                Arg.Any<SimilarityBatchEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult([
                    BatchResult("item-a", [
                        Similarity("item-1", 0.90m),
                        Similarity("item-2", 0.80m)
                    ]),
                    BatchResult("item-b", [
                        Similarity("item-1", 0.90m),
                        Similarity("unknown", 0.80m)
                    ])
                ])));

        var result = await new EvaluateHistoricalSimilarityService(corpus, candidateService, client)
            .EvaluateBatchAsync(
                [
                    new HistoricalSimilarityBatchQuery("item-a", Query),
                    new HistoricalSimilarityBatchQuery("item-b", Query)
                ],
                TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.Completed, result["item-a"].Status);
        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, result["item-b"].Status);
        Assert.Equal("AI2_SIMILARITY_INVALID_CORRELATION", result["item-b"].FailureCode);
    }

    [Fact]
    public async Task EvaluateBatchAsync_WithFifteenGroupsAndOneHundredFiftyCandidates_UsesThreeSafeChunks()
    {
        var candidates = CreateCandidates(10);
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Arg.Any<HistoricalCandidateQuery>()).Returns(candidates);
        var client = new FakeBatchSimilarityClient(request =>
            Ai2SimilarityBatchClientResult.Succeeded(new SimilarityBatchEvaluationResult(
                request.Requests.Select(value => BatchResult(
                    value.RequestId,
                    value.Candidates.Select(candidate =>
                        Similarity(candidate.CandidateId, 0.85m)).ToArray())).ToArray())));
        var service = new EvaluateHistoricalSimilarityService(
            corpus,
            candidateService,
            client);

        var result = await service.EvaluateBatchAsync(
            Enumerable.Range(1, 15)
                .Select(index => new HistoricalSimilarityBatchQuery(
                    $"item-{index}",
                    Query with { System = index.ToString() }))
                .ToArray(),
            TestContext.Current.CancellationToken);

        Assert.Equal(15, result.Count);
        Assert.All(result.Values, value =>
            Assert.Equal(HistoricalSimilarityStatus.Completed, value.Status));
        Assert.Equal(3, client.Requests.Count);
        Assert.All(client.Requests, request =>
        {
            Assert.True(request.Requests.Count <= 5);
            Assert.True(request.Requests.Sum(value => value.Candidates.Count) <= 50);
        });
    }

    [Fact]
    public async Task EvaluateBatchAsync_WhenMiddleChunkFails_OnlyThatChunkFallsBack()
    {
        var candidates = CreateCandidates(10);
        var corpus = LoadedCorpus();
        var candidateService = Substitute.For<IHistoricalComparableCandidateService>();
        candidateService.Find(Arg.Any<HistoricalCandidateQuery>()).Returns(candidates);
        var callIndex = 0;
        var client = new FakeBatchSimilarityClient(request =>
        {
            callIndex++;
            if (callIndex == 2)
            {
                return Ai2SimilarityBatchClientResult.Failed(
                    "AI2_SIMILARITY_REMOTE_ERROR");
            }

            return Ai2SimilarityBatchClientResult.Succeeded(
                new SimilarityBatchEvaluationResult(
                    request.Requests.Select(value => BatchResult(
                        value.RequestId,
                        value.Candidates.Select(candidate =>
                            Similarity(candidate.CandidateId, 0.85m)).ToArray()))
                    .ToArray()));
        });
        var service = new EvaluateHistoricalSimilarityService(
            corpus,
            candidateService,
            client);

        var result = await service.EvaluateBatchAsync(
            Enumerable.Range(1, 15)
                .Select(index => new HistoricalSimilarityBatchQuery(
                    $"item-{index}",
                    Query with { System = index.ToString() }))
                .ToArray(),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(HistoricalSimilarityStatus.Completed, result["item-1"].Status);
        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, result["item-6"].Status);
        Assert.Equal("AI2_SIMILARITY_REMOTE_ERROR", result["item-6"].FailureCode);
        Assert.Equal(HistoricalSimilarityStatus.Completed, result["item-11"].Status);
        Assert.All(
            Enumerable.Range(6, 5).Select(index => result[$"item-{index}"]),
            value =>
            {
                Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure, value.Status);
                Assert.All(value.Candidates, candidate =>
                    Assert.Null(candidate.Similarity));
            });
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

    private static HistoricalComparableCandidate[] CreateCandidates(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new HistoricalComparableCandidate(
                $"quote-{index}",
                $"candidate-{index}",
                $"V-{index:00}",
                $"Ventana {index}",
                100m + index,
                100m + index,
                "VENTANA",
                "8025",
                "TEMPLADO",
                6m,
                "MONOLITICO",
                "CORREDIZA",
                3090m,
                1900m,
                5.87m,
                1m,
                "NEGRO",
                80m,
                ["category", "glass"],
                ["system"],
                false))
            .ToArray();

    private static SimilarityCandidateResult Similarity(string id, decimal score) =>
        new(id, score, "HIGH", ["glass"], ["system"], "Comparacion tecnica.", 0.9m);

    private static SimilarityBatchResultItem BatchResult(
        string requestId,
        IReadOnlyList<SimilarityCandidateResult> candidates) =>
        new(requestId, "COMPLETED", candidates);

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

    private sealed class FakeBatchSimilarityClient(
        Func<SimilarityBatchEvaluationRequest, Ai2SimilarityBatchClientResult> handler)
        : IAi2SimilarityClient
    {
        public List<SimilarityBatchEvaluationRequest> Requests { get; } = [];

        public Task<Ai2SimilarityClientResult> EvaluateAsync(
            SimilarityEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Ai2SimilarityBatchClientResult> EvaluateBatchAsync(
            SimilarityBatchEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }
}
