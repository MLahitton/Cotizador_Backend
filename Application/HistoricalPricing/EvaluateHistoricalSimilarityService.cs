using Application.Common.Abstractions.HistoricalPricing;

namespace Application.HistoricalPricing;

public sealed class EvaluateHistoricalSimilarityService : IHistoricalSimilarityEvaluationService
{
    private const string InvalidCorrelation = "AI2_SIMILARITY_INVALID_CORRELATION";
    private readonly IHistoricalQuoteCorpus _corpus;
    private readonly IHistoricalComparableCandidateService _candidateService;
    private readonly IAi2SimilarityClient _similarityClient;

    public EvaluateHistoricalSimilarityService(
        IHistoricalQuoteCorpus corpus,
        IHistoricalComparableCandidateService candidateService,
        IAi2SimilarityClient similarityClient)
    {
        _corpus = corpus;
        _candidateService = candidateService;
        _similarityClient = similarityClient;
    }

    public async Task<HistoricalSimilarityEvaluationResult> EvaluateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_corpus.Current.IsAvailable)
        {
            await _corpus.ReloadAsync(cancellationToken);
        }

        var shortlist = _candidateService.Find(query);
        if (shortlist.Count == 0)
        {
            return new HistoricalSimilarityEvaluationResult(
                HistoricalSimilarityStatus.Completed, [], null);
        }

        var request = new SimilarityEvaluationRequest(
            MapElement(query),
            shortlist.Select(MapCandidate).ToArray());
        Ai2SimilarityClientResult clientResult;
        try
        {
            clientResult = await _similarityClient.EvaluateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TechnicalFailure(shortlist, "AI2_SIMILARITY_CLIENT_ERROR");
        }

        if (!clientResult.IsSuccess || clientResult.Evaluation is null)
        {
            return TechnicalFailure(
                shortlist,
                clientResult.FailureCode ?? "AI2_SIMILARITY_CLIENT_ERROR");
        }

        var sentIds = request.Candidates.Select(value => value.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        var received = clientResult.Evaluation.Candidates;
        var receivedIds = received.Select(value => value.CandidateId).ToArray();
        if (receivedIds.Length != sentIds.Count
            || receivedIds.Distinct(StringComparer.Ordinal).Count() != receivedIds.Length
            || receivedIds.Any(value => !sentIds.Contains(value))
            || sentIds.Any(value => !receivedIds.Contains(value, StringComparer.Ordinal)))
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

    private static SimilarityElementInput MapElement(HistoricalCandidateQuery query) =>
        new("backend-historical-query", query.Category, query.System, query.Glass, query.GlassThickness, query.GlassComposition,
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
}
