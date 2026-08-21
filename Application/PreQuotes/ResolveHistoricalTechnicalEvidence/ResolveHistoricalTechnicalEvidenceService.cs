using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;

namespace Application.PreQuotes.ResolveHistoricalTechnicalEvidence;

public sealed class ResolveHistoricalTechnicalEvidenceService
{
    private const int MaxExamplesPerSystem = 3;
    private readonly IHistoricalSimilarityEvaluationService _similarityService;
    private readonly IProductSystemCatalogRepository _productSystems;
    private readonly ISgTechnicalSelector _selector;

    public ResolveHistoricalTechnicalEvidenceService(
        IHistoricalSimilarityEvaluationService similarityService,
        IProductSystemCatalogRepository productSystems,
        ISgTechnicalSelector selector)
    {
        _similarityService = similarityService;
        _productSystems = productSystems;
        _selector = selector;
    }

    public async Task<HistoricalTechnicalEvidenceSelectionResult> ResolveAsync(
        SgTechnicalSelectionInput input,
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var similarity = await _similarityService.EvaluateAsync(query, cancellationToken);
        var evidence = similarity.Status == HistoricalSimilarityStatus.Completed
            ? await BuildEvidenceAsync(similarity.Candidates, cancellationToken)
            : [];

        var selection = await _selector.SelectAsync(
            input with { HistoricalSystemEvidence = evidence },
            cancellationToken);

        return new HistoricalTechnicalEvidenceSelectionResult(
            selection,
            similarity.Status,
            similarity.FailureCode,
            evidence);
    }

    private async Task<IReadOnlyList<SgHistoricalSystemEvidence>> BuildEvidenceAsync(
        IReadOnlyList<HistoricalSimilarityCandidateResult> candidates,
        CancellationToken cancellationToken)
    {
        var systems = await _productSystems.ListActiveSelectableAsync(cancellationToken);
        if (systems.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var groups = candidates
            .Where(value => value.Similarity is not null
                && IsValidSimilarity(value.Similarity.SimilarityScore))
            .Select(value => new
            {
                Result = value,
                System = FindMatchingSystem(value.Candidate.System, systems)
            })
            .Where(value => value.System is not null)
            .GroupBy(value => value.System!.Code, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(value => value.Result.Similarity!.SimilarityScore)
                    .ToArray();
                var scores = ordered
                    .Select(value => value.Result.Similarity!.SimilarityScore)
                    .ToArray();

                return new SgHistoricalSystemEvidence(
                    group.Key,
                    scores.Max(),
                    scores.Average(),
                    ordered.Length,
                    ordered.Take(MaxExamplesPerSystem)
                        .Select(value => MapExample(value.Result))
                        .ToArray());
            })
            .OrderByDescending(value => value.BestSimilarity)
            .ThenByDescending(value => value.SupportCount)
            .ThenBy(value => value.ProductSystemCode, StringComparer.Ordinal)
            .ToArray();

        return groups;
    }

    private static ProductSystemCatalogReadModel? FindMatchingSystem(
        string? historicalSystem,
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
    {
        var historical = DeterministicSgTechnicalSelector
            .NormalizeTechnicalText(historicalSystem);
        if (historical.Length == 0)
        {
            return null;
        }

        return systems.FirstOrDefault(system =>
            MatchesSystemText(historical, system.Code)
            || MatchesSystemText(historical, system.Name)
            || MatchesSystemText(historical, system.TechnicalName)
            || MatchesSystemText(historical, system.CommercialName)
            || MatchesSystemText(historical, system.Family)
            || MatchesSystemText(historical, system.Series));
    }

    private static bool MatchesSystemText(string historical, string? catalogValue)
    {
        var catalog = DeterministicSgTechnicalSelector
            .NormalizeTechnicalText(catalogValue);
        return catalog.Length > 0
            && (historical == catalog
                || historical.Contains(catalog, StringComparison.Ordinal)
                || catalog.Contains(historical, StringComparison.Ordinal));
    }

    private static SgHistoricalSystemExample MapExample(
        HistoricalSimilarityCandidateResult result)
    {
        var similarity = result.Similarity!;
        return new SgHistoricalSystemExample(
            result.Candidate.HistoricalItemId,
            result.Candidate.HistoricalQuoteId,
            result.Candidate.HistoricalReference,
            similarity.SimilarityScore,
            similarity.MatchedFeatures,
            similarity.Differences,
            similarity.TechnicalExplanation);
    }

    private static bool IsValidSimilarity(decimal value) =>
        value is >= 0m and <= 1m;
}

public sealed record HistoricalTechnicalEvidenceSelectionResult(
    SgTechnicalSelectionResult Selection,
    HistoricalSimilarityStatus SimilarityStatus,
    string? SimilarityFailureCode,
    IReadOnlyList<SgHistoricalSystemEvidence> HistoricalEvidence);
