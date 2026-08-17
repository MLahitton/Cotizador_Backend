using System.Text.Json.Serialization;

namespace Application.Common.Abstractions.HistoricalPricing;

public sealed record SimilarityElementInput(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("glass_family")] string? GlassFamily,
    [property: JsonPropertyName("glass_thickness")] decimal? GlassThickness,
    [property: JsonPropertyName("glass_composition")] string? GlassComposition,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("width_mm")] decimal? WidthMm,
    [property: JsonPropertyName("height_mm")] decimal? HeightMm,
    [property: JsonPropertyName("area_m2")] decimal? AreaM2,
    [property: JsonPropertyName("quantity")] decimal? Quantity,
    [property: JsonPropertyName("finish")] string? Finish);

public sealed record SimilarityHistoricalCandidateInput(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("quote_id")] string QuoteId,
    [property: JsonPropertyName("historical_item_id")] string HistoricalItemId,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("system")] string? System,
    [property: JsonPropertyName("glass_family")] string? GlassFamily,
    [property: JsonPropertyName("glass_thickness")] decimal? GlassThickness,
    [property: JsonPropertyName("glass_composition")] string? GlassComposition,
    [property: JsonPropertyName("configuration")] string? Configuration,
    [property: JsonPropertyName("width_mm")] decimal? WidthMm,
    [property: JsonPropertyName("height_mm")] decimal? HeightMm,
    [property: JsonPropertyName("area_m2")] decimal? AreaM2,
    [property: JsonPropertyName("quantity")] decimal? Quantity,
    [property: JsonPropertyName("finish")] string? Finish,
    [property: JsonPropertyName("backend_preliminary_score")] decimal BackendPreliminaryScore,
    [property: JsonPropertyName("matched_signals")] IReadOnlyList<string> MatchedSignals,
    [property: JsonPropertyName("missing_signals")] IReadOnlyList<string> MissingSignals);

public sealed record SimilarityEvaluationRequest(
    [property: JsonPropertyName("element")] SimilarityElementInput Element,
    [property: JsonPropertyName("candidates")] IReadOnlyList<SimilarityHistoricalCandidateInput> Candidates);

public sealed record SimilarityCandidateResult(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("similarity_score")] decimal SimilarityScore,
    [property: JsonPropertyName("similarity_level")] string SimilarityLevel,
    [property: JsonPropertyName("matched_features")] IReadOnlyList<string> MatchedFeatures,
    [property: JsonPropertyName("differences")] IReadOnlyList<string> Differences,
    [property: JsonPropertyName("technical_explanation")] string TechnicalExplanation,
    [property: JsonPropertyName("confidence")] decimal Confidence);

public sealed record SimilarityEvaluationResult(
    [property: JsonPropertyName("candidates")] IReadOnlyList<SimilarityCandidateResult> Candidates);

public sealed record Ai2SimilarityClientResult(
    bool IsSuccess,
    SimilarityEvaluationResult? Evaluation,
    string? FailureCode)
{
    public static Ai2SimilarityClientResult Succeeded(SimilarityEvaluationResult evaluation) =>
        new(true, evaluation, null);

    public static Ai2SimilarityClientResult Failed(string failureCode) =>
        new(false, null, failureCode);
}

public interface IAi2SimilarityClient
{
    Task<Ai2SimilarityClientResult> EvaluateAsync(
        SimilarityEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public enum HistoricalSimilarityStatus
{
    Completed = 1,
    TechnicalFailure = 2
}

public sealed record HistoricalSimilarityCandidateResult(
    HistoricalComparableCandidate Candidate,
    SimilarityCandidateResult? Similarity);

public sealed record HistoricalSimilarityEvaluationResult(
    HistoricalSimilarityStatus Status,
    IReadOnlyList<HistoricalSimilarityCandidateResult> Candidates,
    string? FailureCode);

public interface IHistoricalSimilarityEvaluationService
{
    Task<HistoricalSimilarityEvaluationResult> EvaluateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default);
}
