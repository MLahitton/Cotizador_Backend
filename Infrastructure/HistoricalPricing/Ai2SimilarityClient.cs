using System.Net.Http.Json;
using Application.Common.Abstractions.HistoricalPricing;

namespace Infrastructure.HistoricalPricing;

public sealed class Ai2SimilarityClient : IAi2SimilarityClient
{
    private readonly HttpClient _httpClient;
    private readonly Ai2SimilarityOptions _options;

    public Ai2SimilarityClient(HttpClient httpClient, Ai2SimilarityOptions options)
    {
        _httpClient = httpClient;
        _options = options;
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
}
