using System.Net.Http.Json;
using Application.Common.Abstractions.HistoricalPricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.HistoricalPricing;

public sealed class Ai2SimilarityClient : IAi2SimilarityClient
{
    private readonly HttpClient _httpClient;
    private readonly Ai2SimilarityOptions _options;
    private readonly ILogger<Ai2SimilarityClient> _logger;

    public Ai2SimilarityClient(
        HttpClient httpClient,
        Ai2SimilarityOptions options,
        ILogger<Ai2SimilarityClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<Ai2SimilarityClient>.Instance;
    }

    public async Task<Ai2SimilarityClientResult> EvaluateAsync(
        SimilarityEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_options.Endpoint is null)
        {
            return Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_NOT_CONFIGURED");
        }

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _options.Endpoint, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_REMOTE_ERROR");
            }

            var evaluation = await response.Content.ReadFromJsonAsync<SimilarityEvaluationResult>(
                cancellationToken: cancellationToken);
            return evaluation is null
                ? Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_INVALID_RESPONSE")
                : Ai2SimilarityClientResult.Succeeded(evaluation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_TRANSPORT_ERROR");
        }
        catch (System.Text.Json.JsonException)
        {
            return Ai2SimilarityClientResult.Failed("AI2_SIMILARITY_INVALID_RESPONSE");
        }
    }

    public async Task<Ai2SimilarityBatchClientResult> EvaluateBatchAsync(
        SimilarityBatchEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var itemGroupCount = request.Requests.Count;
        var candidateCount = request.Requests.Sum(value => value.Candidates.Count);
        if (_options.BatchEndpoint is null)
        {
            LogBatch(
                itemGroupCount,
                candidateCount,
                0,
                "configuration",
                null,
                "AI2_SIMILARITY_NOT_CONFIGURED");
            return Ai2SimilarityBatchClientResult.Failed(
                "AI2_SIMILARITY_NOT_CONFIGURED");
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _options.BatchEndpoint, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                started.Stop();
                LogBatch(
                    itemGroupCount,
                    candidateCount,
                    started.ElapsedMilliseconds,
                    "provider_http_status",
                    (int)response.StatusCode,
                    "AI2_SIMILARITY_REMOTE_ERROR");
                return Ai2SimilarityBatchClientResult.Failed(
                    "AI2_SIMILARITY_REMOTE_ERROR");
            }

            var evaluation = await response.Content
                .ReadFromJsonAsync<SimilarityBatchEvaluationResult>(
                    cancellationToken: cancellationToken);
            started.Stop();
            if (evaluation is null)
            {
                LogBatch(
                    itemGroupCount,
                    candidateCount,
                    started.ElapsedMilliseconds,
                    "response_body",
                    (int)response.StatusCode,
                    "AI2_SIMILARITY_INVALID_RESPONSE");
                return Ai2SimilarityBatchClientResult.Failed(
                    "AI2_SIMILARITY_INVALID_RESPONSE");
            }

            LogBatch(
                itemGroupCount,
                candidateCount,
                started.ElapsedMilliseconds,
                "completed",
                (int)response.StatusCode,
                null);
            return Ai2SimilarityBatchClientResult.Succeeded(evaluation);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            started.Stop();
            LogBatch(
                itemGroupCount,
                candidateCount,
                started.ElapsedMilliseconds,
                "http_timeout",
                null,
                "AI2_SIMILARITY_TIMEOUT");
            return Ai2SimilarityBatchClientResult.Failed(
                "AI2_SIMILARITY_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            started.Stop();
            LogBatch(
                itemGroupCount,
                candidateCount,
                started.ElapsedMilliseconds,
                "transport",
                null,
                "AI2_SIMILARITY_TRANSPORT_ERROR");
            return Ai2SimilarityBatchClientResult.Failed(
                "AI2_SIMILARITY_TRANSPORT_ERROR");
        }
        catch (System.Text.Json.JsonException)
        {
            started.Stop();
            LogBatch(
                itemGroupCount,
                candidateCount,
                started.ElapsedMilliseconds,
                "json",
                null,
                "AI2_SIMILARITY_INVALID_RESPONSE");
            return Ai2SimilarityBatchClientResult.Failed(
                "AI2_SIMILARITY_INVALID_RESPONSE");
        }
    }

    private void LogBatch(
        int itemGroupCount,
        int candidateCount,
        long elapsedMs,
        string failedStage,
        int? providerStatusCode,
        string? failureCode)
    {
        _logger.LogInformation(
            "[NEWPIPE-AI2-SIMILARITY-BATCH] itemGroupCount={ItemGroupCount} candidateCount={CandidateCount} elapsedMs={ElapsedMs} failedStage={FailedStage} exceptionType={ExceptionType} providerStatusCode={ProviderStatusCode} failureCode={FailureCode}",
            itemGroupCount,
            candidateCount,
            elapsedMs,
            failedStage,
            failureCode is null ? null : "Ai2SimilarityClientFailure",
            providerStatusCode,
            failureCode);
    }
}
