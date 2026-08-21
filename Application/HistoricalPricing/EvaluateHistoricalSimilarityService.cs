using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.HistoricalPricing;

public sealed class EvaluateHistoricalSimilarityService : IHistoricalSimilarityEvaluationService
{
    private const string InvalidCorrelation = "AI2_SIMILARITY_INVALID_CORRELATION";
    private readonly IHistoricalQuoteCorpus _corpus;
    private readonly IHistoricalComparableCandidateService _candidateService;
    private readonly IAi2SimilarityClient _similarityClient;
    private readonly HistoricalSimilarityBatchOptions _batchOptions;
    private readonly ILogger<EvaluateHistoricalSimilarityService> _logger;

    public EvaluateHistoricalSimilarityService(
        IHistoricalQuoteCorpus corpus,
        IHistoricalComparableCandidateService candidateService,
        IAi2SimilarityClient similarityClient,
        HistoricalSimilarityBatchOptions? batchOptions = null,
        ILogger<EvaluateHistoricalSimilarityService>? logger = null)
    {
        _corpus = corpus;
        _candidateService = candidateService;
        _similarityClient = similarityClient;
        _batchOptions = batchOptions ?? HistoricalSimilarityBatchOptions.Default;
        _logger = logger ?? NullLogger<EvaluateHistoricalSimilarityService>.Instance;
    }

    public async Task<HistoricalSimilarityEvaluationResult> EvaluateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var batch = await EvaluateBatchAsync(
            [new HistoricalSimilarityBatchQuery("single", query)],
            cancellationToken);
        return batch["single"];
    }

    public async Task<IReadOnlyDictionary<string, HistoricalSimilarityEvaluationResult>>
        EvaluateBatchAsync(
            IReadOnlyList<HistoricalSimilarityBatchQuery> queries,
            CancellationToken cancellationToken = default)
    {
        if (queries.Count == 0)
        {
            return new Dictionary<string, HistoricalSimilarityEvaluationResult>(
                StringComparer.Ordinal);
        }

        if (queries.Select(value => value.RequestId)
            .Distinct(StringComparer.Ordinal).Count() != queries.Count)
        {
            throw new ArgumentException(
                "Los requestId de similarity historica deben ser unicos.",
                nameof(queries));
        }

        var context = NewPipePerformanceContext.Current;
        var corpusLoadedFromMemory = _corpus.Current.IsAvailable;
        if (!_corpus.Current.IsAvailable)
        {
            var reloadStarted = System.Diagnostics.Stopwatch.StartNew();
            await _corpus.ReloadAsync(cancellationToken);
            reloadStarted.Stop();
            context?.RecordCorpusReload(reloadStarted.ElapsedMilliseconds);
            LogPerf(
                "HISTORICAL_CORPUS_RELOAD",
                reloadStarted.ElapsedMilliseconds,
                ("corpusLoadedFromMemory", false),
                ("reloadCount", context?.CorpusReloadCount ?? 1));
        }
        else
        {
            LogPerf(
                "HISTORICAL_CORPUS_AVAILABLE",
                0,
                ("corpusLoadedFromMemory", true),
                ("reloadCount", context?.CorpusReloadCount ?? 0));
        }

        var prepared = queries.Select(query =>
        {
            var findStarted = System.Diagnostics.Stopwatch.StartNew();
            var shortlist = _candidateService.Find(query.Query);
            findStarted.Stop();
            context?.RecordHistoricalShortlist(findStarted.ElapsedMilliseconds);
            LogPerf(
                "HISTORICAL_SHORTLIST",
                findStarted.ElapsedMilliseconds,
                ("requestId", query.RequestId),
                ("candidateCount", shortlist.Count),
                ("corpusLoadedFromMemory", corpusLoadedFromMemory),
                ("reloadCount", context?.CorpusReloadCount ?? 0));

            return new PreparedSimilarityRequest(
                query.RequestId,
                query.Query,
                shortlist);
        }).ToArray();

        var results = new Dictionary<string, HistoricalSimilarityEvaluationResult>(
            StringComparer.Ordinal);
        foreach (var item in prepared.Where(value => value.Shortlist.Count == 0))
        {
            results[item.RequestId] = new HistoricalSimilarityEvaluationResult(
                HistoricalSimilarityStatus.Completed, [], null);
        }

        var pending = prepared
            .Where(value => value.Shortlist.Count > 0)
            .ToArray();
        var chunks = Chunks(pending);
        for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            var request = new SimilarityBatchEvaluationRequest(
                chunk.Select(value => new SimilarityBatchRequestItem(
                    value.RequestId,
                    MapElement(value.Query, value.RequestId),
                    value.Shortlist.Select(MapCandidate).ToArray())).ToArray());

            Ai2SimilarityBatchClientResult clientResult;
            var similarityStarted = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                clientResult = await _similarityClient.EvaluateBatchAsync(
                    request,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                similarityStarted.Stop();
                var candidateCount = request.Requests.Sum(value =>
                    value.Candidates.Count);
                context?.RecordSimilarityCall(candidateCount);
                LogSimilarityBatch(
                    chunkIndex,
                    similarityStarted.ElapsedMilliseconds,
                    request,
                    false,
                    "AI2_SIMILARITY_CLIENT_EXCEPTION");
                AddFailures(results, chunk, "AI2_SIMILARITY_CLIENT_ERROR");
                continue;
            }

            similarityStarted.Stop();
            context?.RecordSimilarityCall(request.Requests.Sum(value =>
                value.Candidates.Count));
            LogSimilarityBatch(
                chunkIndex,
                similarityStarted.ElapsedMilliseconds,
                request,
                clientResult.IsSuccess,
                clientResult.FailureCode);

            if (!clientResult.IsSuccess || clientResult.Evaluation is null)
            {
                AddFailures(
                    results,
                    chunk,
                    clientResult.FailureCode ?? "AI2_SIMILARITY_CLIENT_ERROR");
                continue;
            }

            AddSuccessfulChunkResults(results, chunk, clientResult.Evaluation);
        }

        return results;
    }

    private void LogSimilarityBatch(
        int chunkIndex,
        long elapsedMs,
        SimilarityBatchEvaluationRequest request,
        bool success,
        string? failureCode)
    {
        var candidateCount = request.Requests.Sum(value => value.Candidates.Count);
        LogPerf(
            "CALL_AI2_SIMILARITY",
            elapsedMs,
            ("requestCount", request.Requests.Count),
            ("candidateCount", candidateCount),
            ("chunkIndex", chunkIndex),
            ("success", success));
        LogPerf(
            "SIMILARITY_BATCH",
            elapsedMs,
            ("batchRequestCount", 1),
            ("chunkIndex", chunkIndex),
            ("itemGroupCount", request.Requests.Count),
            ("candidateCountTotal", candidateCount),
            ("llmCallCount", 1),
            ("failedChunkCount", success ? 0 : 1),
            ("failureCode", failureCode),
            ("success", success));
    }

    private static void AddSuccessfulChunkResults(
        IDictionary<string, HistoricalSimilarityEvaluationResult> results,
        IReadOnlyList<PreparedSimilarityRequest> chunk,
        SimilarityBatchEvaluationResult evaluation)
    {
        var sentIds = chunk.Select(value => value.RequestId)
            .ToHashSet(StringComparer.Ordinal);
        var receivedIds = evaluation.Results.Select(value => value.RequestId)
            .ToArray();
        if (receivedIds.Length != sentIds.Count
            || receivedIds.Distinct(StringComparer.Ordinal).Count()
                != receivedIds.Length
            || receivedIds.Any(value => !sentIds.Contains(value))
            || sentIds.Any(value => !receivedIds.Contains(
                value,
                StringComparer.Ordinal)))
        {
            AddFailures(results, chunk, InvalidCorrelation);
            return;
        }

        var resultsById = evaluation.Results.ToDictionary(
            value => value.RequestId,
            StringComparer.Ordinal);
        foreach (var item in chunk)
        {
            var response = resultsById[item.RequestId];
            if (!IsSuccessfulStatus(response.Status))
            {
                results[item.RequestId] = TechnicalFailure(
                    item.Shortlist,
                    response.FailureCode ?? "AI2_SIMILARITY_ITEM_FAILED");
                continue;
            }

            results[item.RequestId] = MapCompletedResult(item.Shortlist, response);
        }
    }

    private static HistoricalSimilarityEvaluationResult MapCompletedResult(
        IReadOnlyList<HistoricalComparableCandidate> shortlist,
        SimilarityBatchResultItem response)
    {
        var sentIds = shortlist.Select(value => value.HistoricalItemId)
            .ToHashSet(StringComparer.Ordinal);
        var received = response.Candidates;
        var receivedIds = received.Select(value => value.CandidateId).ToArray();
        if (receivedIds.Length != sentIds.Count
            || receivedIds.Distinct(StringComparer.Ordinal).Count()
                != receivedIds.Length
            || receivedIds.Any(value => !sentIds.Contains(value))
            || sentIds.Any(value => !receivedIds.Contains(
                value,
                StringComparer.Ordinal)))
        {
            return TechnicalFailure(shortlist, InvalidCorrelation);
        }

        var candidatesById = shortlist.ToDictionary(
            value => value.HistoricalItemId,
            StringComparer.Ordinal);
        return new HistoricalSimilarityEvaluationResult(
            HistoricalSimilarityStatus.Completed,
            received.Select(value => new HistoricalSimilarityCandidateResult(
                candidatesById[value.CandidateId], value)).ToArray(),
            null);
    }

    private static bool IsSuccessfulStatus(string value) =>
        string.Equals(value, "COMPLETED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "SUCCESS", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "OK", StringComparison.OrdinalIgnoreCase);

    private static void AddFailures(
        IDictionary<string, HistoricalSimilarityEvaluationResult> results,
        IReadOnlyList<PreparedSimilarityRequest> chunk,
        string failureCode)
    {
        foreach (var item in chunk)
        {
            results[item.RequestId] = TechnicalFailure(
                item.Shortlist,
                failureCode);
        }
    }

    private IReadOnlyList<PreparedSimilarityRequest>[] Chunks(
        IReadOnlyList<PreparedSimilarityRequest> requests)
    {
        var chunks = new List<IReadOnlyList<PreparedSimilarityRequest>>();
        var current = new List<PreparedSimilarityRequest>();
        var candidateCount = 0;
        var maxRequestItems = _batchOptions.SafeMaxItemGroupsPerBatch;
        var maxCandidateItems = _batchOptions.SafeMaxCandidatesPerBatch;
        foreach (var request in requests)
        {
            var nextCandidateCount = candidateCount + request.Shortlist.Count;
            if (current.Count > 0
                && (current.Count == maxRequestItems
                    || nextCandidateCount > maxCandidateItems))
            {
                chunks.Add(current.ToArray());
                current.Clear();
                candidateCount = 0;
            }

            current.Add(request);
            candidateCount += request.Shortlist.Count;
        }

        if (current.Count > 0)
        {
            chunks.Add(current.ToArray());
        }

        return chunks.ToArray();
    }

    private static SimilarityElementInput MapElement(
        HistoricalCandidateQuery query,
        string requestId) =>
        new(requestId, query.Category, query.System, query.Glass, query.GlassThickness, query.GlassComposition,
            query.Configuration, query.Width, query.Height, query.Area,
            query.Quantity, query.Finish);

    private static SimilarityHistoricalCandidateInput MapCandidate(
        HistoricalComparableCandidate candidate) =>
        new(candidate.HistoricalItemId, candidate.HistoricalQuoteId,
            candidate.HistoricalItemId, candidate.HistoricalReference,
            candidate.Description, candidate.Category, candidate.System,
            candidate.Glass, candidate.GlassThickness,
            candidate.GlassComposition, candidate.Configuration,
            candidate.Width, candidate.Height, candidate.Area,
            candidate.Quantity, candidate.Finish,
            candidate.PreliminaryScore / HistoricalCandidateRankingWeights.MaximumScore,
            candidate.MatchedSignals,
            candidate.MissingSignals);

    private static HistoricalSimilarityEvaluationResult TechnicalFailure(
        IReadOnlyList<HistoricalComparableCandidate> shortlist,
        string failureCode) =>
        new(HistoricalSimilarityStatus.TechnicalFailure,
            shortlist.Select(value => new HistoricalSimilarityCandidateResult(value, null)).ToArray(),
            failureCode);

    private sealed record PreparedSimilarityRequest(
        string RequestId,
        HistoricalCandidateQuery Query,
        IReadOnlyList<HistoricalComparableCandidate> Shortlist);

    private void LogPerf(
        string stage,
        long elapsedMs,
        params (string Name, object? Value)[] values)
    {
        var context = NewPipePerformanceContext.Current;
        var detail = string.Join(
            " ",
            values.Select(value => $"{value.Name}={value.Value}"));
        _logger.LogInformation(
            "[NEWPIPE-PERF] RequirementId={RequirementId} AttemptId={AttemptId} Stage={Stage} ElapsedMs={ElapsedMs} {Detail}",
            context?.RequirementId,
            context?.AttemptId,
            stage,
            elapsedMs,
            detail);
    }
}
