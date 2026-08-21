using Application.Common.Abstractions.HistoricalPricing;

namespace Application.HistoricalPricing;

public sealed class HistoricalTechnicalPriceEstimator : IHistoricalTechnicalPriceEstimator
{
    private const string PricingSource = "HISTORICAL_COMPARABLES";
    private readonly IHistoricalSimilarityEvaluationService _similarityService;

    public HistoricalTechnicalPriceEstimator(
        IHistoricalSimilarityEvaluationService similarityService)
    {
        _similarityService = similarityService;
    }

    public async Task<HistoricalTechnicalPriceEstimate> EstimateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await _similarityService.EvaluateAsync(query, cancellationToken);
        var assumptions = new List<string>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var newUnitArea = ResolveNewUnitArea(query);
        if (newUnitArea is null)
        {
            missing.Add("unitArea");
            assumptions.Add("Sin area nueva: se usa PublicUnitPrice sin normalizacion por area.");
        }

        var fallback = evaluation.Status == HistoricalSimilarityStatus.TechnicalFailure;
        if (fallback)
        {
            assumptions.Add("Similarity AI2 no disponible; se usa score tecnico Backend con confianza degradada.");
        }

        var comparables = new List<HistoricalTechnicalPriceComparable>();
        var criticalMismatchIds = new HashSet<string>(StringComparer.Ordinal);
        var systemMismatchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in evaluation.Candidates)
        {
            var candidate = value.Candidate;
            if (candidate.PublicUnitPrice <= 0)
            {
                continue;
            }

            var historicalUnitArea = ResolveHistoricalUnitArea(candidate);
            if (historicalUnitArea is null or <= 0)
            {
                missing.Add("historicalUnitArea");
                continue;
            }

            var backendScore = Math.Clamp(
                candidate.PreliminaryScore / HistoricalCandidateRankingWeights.MaximumScore,
                0m,
                1m);
            var similarity = value.Similarity;
            var ai2Score = similarity?.SimilarityScore;
            var weight = backendScore;
            if (candidate.HasAreaMismatch)
            {
                weight *= HistoricalTechnicalPricingRules.AreaMismatchWeightFactor;
                assumptions.Add($"Comparable {candidate.HistoricalItemId} reducido por HistoricalAreaMismatch.");
            }
            if (weight <= 0)
            {
                continue;
            }

            var projectedPrice = ProjectPrice(
                candidate.PublicUnitPrice,
                historicalUnitArea.Value,
                newUnitArea);
            var hasCriticalMismatch = HasCriticalTechnicalMismatch(query, candidate);
            if (hasCriticalMismatch)
            {
                criticalMismatchIds.Add(candidate.HistoricalItemId);
            }
            if (KnownTextMismatch(query.System, candidate.System))
            {
                systemMismatchIds.Add(candidate.HistoricalItemId);
            }
            var isStrong = backendScore >= HistoricalTechnicalPricingRules.StrongBackendScore
                && !candidate.HasAreaMismatch
                && !hasCriticalMismatch;
            comparables.Add(new HistoricalTechnicalPriceComparable(
                candidate.HistoricalItemId,
                candidate.HistoricalQuoteId,
                candidate.HistoricalReference,
                candidate.PublicUnitPrice,
                backendScore,
                ai2Score,
                similarity?.SimilarityLevel,
                weight,
                historicalUnitArea.Value,
                projectedPrice,
                isStrong,
                candidate.HasAreaMismatch));
        }

        var filtered = ExcludeOutliers(comparables, assumptions);
        if (filtered.Count == 0)
        {
            return Empty(evaluation.Candidates.Count, assumptions, missing);
        }

        var weightedValues = filtered.Select(value =>
            new HistoricalWeightedValue(value.ProjectedPrice, value.FinalWeight)).ToArray();
        var minimum = HistoricalTechnicalPriceStatistics.WeightedQuantile(
            weightedValues, HistoricalTechnicalPricingRules.LowerQuantile);
        var expected = HistoricalTechnicalPriceStatistics.WeightedQuantile(
            weightedValues, HistoricalTechnicalPricingRules.ExpectedQuantile);
        var maximum = HistoricalTechnicalPriceStatistics.WeightedQuantile(
            weightedValues, HistoricalTechnicalPricingRules.UpperQuantile);
        if (filtered.Count == 1)
        {
            minimum = expected * (1m - HistoricalTechnicalPricingRules.SingleComparableRangeExpansion);
            maximum = expected * (1m + HistoricalTechnicalPricingRules.SingleComparableRangeExpansion);
            assumptions.Add("Rango ampliado por existir un unico comparable util.");
        }

        var similarityCount = filtered.Count(value => value.Ai2SimilarityScore is not null);
        var strongCount = filtered.Count(value => value.IsStrong);
        var allComparablesHaveCriticalMismatch = filtered.All(value =>
            criticalMismatchIds.Contains(value.CandidateId));
        var totalWeight = filtered.Sum(value => value.FinalWeight);
        var systemMismatchWeight = filtered
            .Where(value => systemMismatchIds.Contains(value.CandidateId))
            .Sum(value => value.FinalWeight);
        var systemMismatchDominates = totalWeight > 0
            && systemMismatchWeight / totalWeight
                >= HistoricalTechnicalPricingRules.SystemMismatchReviewWeightShare;
        if (systemMismatchDominates)
        {
            assumptions.Add("La estimacion depende principalmente de comparables con un sistema conocido diferente.");
            if (!filtered.Any(value => SystemsMatch(query.System,
                    evaluation.Candidates.First(candidate =>
                        candidate.Candidate.HistoricalItemId == value.CandidateId).Candidate.System)))
            {
                missing.Add("NO_EXACT_SYSTEM_COMPARABLE");
            }
        }
        var confidence = CalculateConfidence(
            filtered, expected, fallback, missing.Count > 0, strongCount,
            allComparablesHaveCriticalMismatch);
        var level = ConfidenceLevel(confidence);
        return new HistoricalTechnicalPriceEstimate(
            "COP", minimum, expected, maximum, confidence, level, PricingSource,
            evaluation.Candidates.Count, similarityCount, strongCount,
            systemMismatchDominates
                || level is HistoricalPriceConfidenceLevel.Low or HistoricalPriceConfidenceLevel.Medium,
            assumptions.Distinct(StringComparer.Ordinal).ToArray(), missing.ToArray(),
            filtered.Select(value => value.CandidateId).ToArray(), filtered);
    }

    private static decimal? ResolveNewUnitArea(HistoricalCandidateQuery query)
    {
        if (query.Area is > 0) return query.Area;
        return GeometryArea(query.Width, query.Height);
    }

    public static decimal? ResolveHistoricalUnitArea(HistoricalComparableCandidate candidate)
    {
        if (candidate.Area is not > 0) return null;
        var quantity = candidate.Quantity is > 0 ? candidate.Quantity.Value : 1m;
        if (quantity <= 1m) return candidate.Area;
        var geometry = GeometryArea(candidate.Width, candidate.Height);
        if (geometry is > 0)
        {
            if (RelativeDifference(candidate.Area.Value, geometry.Value) <= 0.10m)
            {
                return candidate.Area;
            }
            if (RelativeDifference(candidate.Area.Value, geometry.Value * quantity) <= 0.10m)
            {
                return candidate.Area / quantity;
            }
        }
        return candidate.Area / quantity;
    }

    private static decimal ProjectPrice(
        decimal publicUnitPrice,
        decimal historicalUnitArea,
        decimal? newUnitArea)
    {
        if (newUnitArea is null) return publicUnitPrice;
        var normalized = publicUnitPrice / historicalUnitArea * newUnitArea.Value;
        var difference = RelativeDifference(historicalUnitArea, newUnitArea.Value);
        if (difference <= HistoricalTechnicalPricingRules.DirectPriceAreaTolerance)
        {
            return publicUnitPrice * 0.75m + normalized * 0.25m;
        }
        if (difference <= HistoricalTechnicalPricingRules.BlendedPriceAreaTolerance)
        {
            return publicUnitPrice * 0.40m + normalized * 0.60m;
        }
        return normalized;
    }

    private static IReadOnlyList<HistoricalTechnicalPriceComparable> ExcludeOutliers(
        IReadOnlyList<HistoricalTechnicalPriceComparable> values,
        ICollection<string> assumptions)
    {
        if (values.Count < 4) return values;
        var weighted = values.Select(value =>
            new HistoricalWeightedValue(value.ProjectedPrice, value.FinalWeight)).ToArray();
        var q1 = HistoricalTechnicalPriceStatistics.WeightedQuantile(weighted, 0.25m);
        var q3 = HistoricalTechnicalPriceStatistics.WeightedQuantile(weighted, 0.75m);
        var iqr = q3 - q1;
        if (iqr <= 0) return values;
        var lower = q1 - HistoricalTechnicalPricingRules.OutlierIqrMultiplier * iqr;
        var upper = q3 + HistoricalTechnicalPricingRules.OutlierIqrMultiplier * iqr;
        var filtered = values.Where(value =>
            value.ProjectedPrice >= lower && value.ProjectedPrice <= upper).ToArray();
        if (filtered.Length != values.Count)
        {
            assumptions.Add($"Se excluyeron {values.Count - filtered.Length} outliers economicos por IQR.");
        }
        return filtered;
    }

    private static decimal CalculateConfidence(
        IReadOnlyList<HistoricalTechnicalPriceComparable> values,
        decimal expected,
        bool fallback,
        bool hasMissingData,
        int strongCount,
        bool allComparablesHaveCriticalMismatch)
    {
        var countComponent = values.Count switch
        {
            1 => 0.20m,
            2 or 3 => 0.40m,
            4 or 5 => 0.60m,
            _ => 0.70m
        };
        var averageWeight = values.Average(value => value.FinalWeight);
        var dispersion = expected <= 0
            ? 1m
            : (values.Max(value => value.ProjectedPrice)
                - values.Min(value => value.ProjectedPrice)) / expected;
        var confidence = countComponent
            + averageWeight * 0.25m
            + (strongCount > 0 ? Math.Min(0.15m, strongCount * 0.05m) : 0m)
            - Math.Min(0.25m, dispersion * 0.20m)
            - (fallback ? 0.20m : 0m)
            - (hasMissingData ? 0.10m : 0m)
            - (values.Any(value => value.HasAreaMismatch) ? 0.10m : 0m);
        confidence = Math.Clamp(confidence, 0m, 1m);
        if (values.Count == 1) confidence = Math.Min(confidence, 0.49m);
        if (values.Count <= 3) confidence = Math.Min(confidence, 0.69m);
        if (strongCount == 0) confidence = Math.Min(confidence, 0.59m);
        if (allComparablesHaveCriticalMismatch)
        {
            confidence = Math.Min(
                confidence,
                HistoricalTechnicalPricingRules.CriticalMismatchConfidenceCap);
        }
        if (fallback) confidence = Math.Min(confidence, 0.39m);
        return confidence;
    }

    private static bool HasCriticalTechnicalMismatch(
        HistoricalCandidateQuery query,
        HistoricalComparableCandidate candidate) =>
        KnownTextMismatch(query.Category, candidate.Category)
        || KnownTextMismatch(query.System, candidate.System)
        || KnownTextMismatch(query.Glass, candidate.Glass)
        || KnownDecimalMismatch(
            query.GlassThickness,
            candidate.GlassThickness,
            HistoricalCandidateRankingWeights.ThicknessTolerance)
        || KnownTextMismatch(query.Configuration, candidate.Configuration);

    private static bool KnownTextMismatch(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && !string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SystemsMatch(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool KnownDecimalMismatch(
        decimal? expected,
        decimal? actual,
        decimal tolerance) =>
        expected is not null
        && actual is not null
        && Math.Abs(expected.Value - actual.Value) > tolerance;

    private static HistoricalPriceConfidenceLevel ConfidenceLevel(decimal score) => score switch
    {
        < 0.35m => HistoricalPriceConfidenceLevel.Low,
        < 0.60m => HistoricalPriceConfidenceLevel.Medium,
        < 0.80m => HistoricalPriceConfidenceLevel.Good,
        _ => HistoricalPriceConfidenceLevel.High
    };

    private static HistoricalTechnicalPriceEstimate Empty(
        int candidateCount,
        ICollection<string> assumptions,
        IReadOnlyCollection<string> missing) =>
        new("COP", null, null, null, 0m, HistoricalPriceConfidenceLevel.Low,
            PricingSource, candidateCount, 0, 0, true,
            assumptions.Append("No existen comparables economicos utilizables.").ToArray(),
            missing.ToArray(), [], []);

    private static decimal? GeometryArea(decimal? width, decimal? height)
    {
        if (width is not > 0 || height is not > 0) return null;
        var area = width.Value * height.Value;
        return width > 50m || height > 50m ? area / 1_000_000m : area;
    }

    private static decimal RelativeDifference(decimal left, decimal right) =>
        Math.Abs(left - right) / Math.Max(Math.Abs(right), 0.0001m);
}

public sealed record HistoricalWeightedValue(decimal Value, decimal Weight);

public static class HistoricalTechnicalPriceStatistics
{
    public static decimal WeightedQuantile(
        IReadOnlyList<HistoricalWeightedValue> values,
        decimal quantile)
    {
        if (values.Count == 0) throw new ArgumentException("Se requieren valores.", nameof(values));
        if (quantile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(quantile));
        var ordered = values.Where(value => value.Weight > 0)
            .OrderBy(value => value.Value).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("Se requieren pesos positivos.", nameof(values));
        var threshold = ordered.Sum(value => value.Weight) * quantile;
        decimal cumulative = 0;
        foreach (var value in ordered)
        {
            cumulative += value.Weight;
            if (cumulative >= threshold) return value.Value;
        }
        return ordered[^1].Value;
    }
}

public static class HistoricalTechnicalPricingRules
{
    public const decimal StrongAi2Score = 0.75m;
    public const decimal StrongBackendScore = 0.75m;
    public const decimal BackendOnlyWeightFactor = 0.40m;
    public const decimal LowSimilarityWeightFactor = 0.10m;
    public const decimal AreaMismatchWeightFactor = 0.25m;
    public const decimal DirectPriceAreaTolerance = 0.10m;
    public const decimal BlendedPriceAreaTolerance = 0.25m;
    public const decimal LowerQuantile = 0.25m;
    public const decimal ExpectedQuantile = 0.50m;
    public const decimal UpperQuantile = 0.75m;
    public const decimal OutlierIqrMultiplier = 1.5m;
    public const decimal SingleComparableRangeExpansion = 0.20m;
    public const decimal CriticalMismatchConfidenceCap = 0.79m;
    public const decimal SystemMismatchReviewWeightShare = 0.50m;
}
