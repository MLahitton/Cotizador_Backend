using System.Net;
using System.Text;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Diagnostics;
using Infrastructure.HistoricalPricing;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.HistoricalPricing;

public sealed class Ai2SimilarityClientTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TraceHeaders_UseExistingContextOnly(bool withContext, bool single)
    {
        var requirement = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        using var scope = withContext ? NewPipePerformanceContext.Begin(requirement, attempt, correlation) : null;
        var handler = new CaptureHandler("{\"results\":[],\"candidates\":[]}");
        using var httpClient = new HttpClient(handler);
        var client = new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(new Uri("http://localhost/similarity/evaluate")));
        var request = CreateBatchRequest();
        if (single)
            await client.EvaluateAsync(new(request.Requests[0].Element, request.Requests[0].Candidates), TestContext.Current.CancellationToken);
        else
            await client.EvaluateBatchAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(withContext ? correlation.ToString("D") : null, handler.CorrelationId);
        Assert.Equal(withContext ? attempt.ToString("D") : null, handler.AttemptId);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(true, "AI2_SIMILARITY_TIMEOUT")]
    [InlineData(false, "AI2_SIMILARITY_TRANSPORT_ERROR")]
    public async Task BatchExceptions_KeepFailureCodes(bool timeout, string code)
    {
        using var httpClient = new HttpClient(new ThrowingHandler(timeout));
        var client = new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(new Uri("http://localhost/similarity/evaluate")));
        var result = await client.EvaluateBatchAsync(CreateBatchRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(code, result.FailureCode);
        Assert.False(result.IsSuccess);
    }

    private sealed class ThrowingHandler(bool timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(timeout ? new TaskCanceledException() : new HttpRequestException());
    }

    [Fact]
    public async Task EvaluateBatchAsync_UsesDerivedBatchEndpointAndSendsRequests()
    {
        var handler = new CaptureHandler("""
            {
              "results": [
                {
                  "request_id": "item-1",
                  "status": "COMPLETED",
                  "candidates": [
                    {
                      "candidate_id": "cand-1",
                      "similarity_score": 0.91,
                      "similarity_level": "HIGH",
                      "matched_features": ["glass"],
                      "differences": [],
                      "technical_explanation": "Comparable.",
                      "confidence": 0.8
                    }
                  ],
                  "failure_code": null
                }
              ],
              "evaluation_source": "AI2_SIMILARITY_BATCH"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new Ai2SimilarityClient(
            httpClient,
            new Ai2SimilarityOptions(
                new Uri("http://127.0.0.1:8000/similarity/evaluate")));

        var result = await client.EvaluateBatchAsync(
            new SimilarityBatchEvaluationRequest([
                new SimilarityBatchRequestItem(
                    "item-1",
                    new SimilarityElementInput(
                        "item-1",
                        "PUERTA",
                        "3831",
                        "templado",
                        6m,
                        "MONOLITICO",
                        "corrediza",
                        3740m,
                        2500m,
                        9.35m,
                        1m,
                        "negro"),
                    [
                        new SimilarityHistoricalCandidateInput(
                            "cand-1",
                            "quote-1",
                            "hist-1",
                            "PV-01",
                            "Puerta historica",
                            "PUERTA",
                            "3831",
                            "templado",
                            6m,
                            "MONOLITICO",
                            "corrediza",
                            3740m,
                            2500m,
                            9.35m,
                            1m,
                            "negro",
                            0.9m,
                            ["category"],
                            [])
                    ])
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "http://127.0.0.1:8000/similarity/evaluate-batch",
            handler.RequestUri!.ToString());
        Assert.Contains("\"requests\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"item-1\"", handler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateBatchAsync_WhenProviderReturnsBadGateway_FailsWithoutRetrying()
    {
        var handler = new CaptureHandler("{}", HttpStatusCode.BadGateway);
        using var httpClient = new HttpClient(handler);
        var client = new Ai2SimilarityClient(
            httpClient,
            new Ai2SimilarityOptions(
                new Uri("http://127.0.0.1:8000/similarity/evaluate")));

        var result = await client.EvaluateBatchAsync(
            CreateBatchRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("AI2_SIMILARITY_REMOTE_ERROR", result.FailureCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(
            "http://127.0.0.1:8000/similarity/evaluate-batch",
            handler.RequestUri!.ToString());
    }

    [Fact]
    public async Task EvaluateBatchAsync_WhenProviderReturnsInvalidJson_FailsControlled()
    {
        var handler = new CaptureHandler("{ invalid json");
        using var httpClient = new HttpClient(handler);
        var client = new Ai2SimilarityClient(
            httpClient,
            new Ai2SimilarityOptions(
                new Uri("http://127.0.0.1:8000/similarity/evaluate")));

        var result = await client.EvaluateBatchAsync(
            CreateBatchRequest(),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("AI2_SIMILARITY_INVALID_RESPONSE", result.FailureCode);
        Assert.Equal(1, handler.CallCount);
    }

    private static SimilarityBatchEvaluationRequest CreateBatchRequest() =>
        new([
            new SimilarityBatchRequestItem(
                "item-1",
                new SimilarityElementInput(
                    "item-1",
                    "PUERTA",
                    "3831",
                    "templado",
                    6m,
                    "MONOLITICO",
                    "corrediza",
                    3740m,
                    2500m,
                    9.35m,
                    1m,
                    "negro"),
                [
                    new SimilarityHistoricalCandidateInput(
                        "cand-1",
                        "quote-1",
                        "hist-1",
                        "PV-01",
                        "Puerta historica",
                        "PUERTA",
                        "3831",
                        "templado",
                        6m,
                        "MONOLITICO",
                        "corrediza",
                        3740m,
                        2500m,
                        9.35m,
                        1m,
                        "negro",
                        0.9m,
                        ["category"],
                        [])
                ])
        ]);

    private sealed class CaptureHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public int CallCount { get; private set; }
        public string? CorrelationId { get; private set; }
        public string? AttemptId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            CorrelationId = request.Headers.TryGetValues("X-Correlation-ID", out var correlations) ? correlations.Single() : null;
            AttemptId = request.Headers.TryGetValues("X-Processing-Attempt-ID", out var attempts) ? attempts.Single() : null;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
