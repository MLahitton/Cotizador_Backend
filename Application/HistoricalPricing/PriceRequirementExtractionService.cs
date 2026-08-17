using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Microsoft.Extensions.Logging;

namespace Application.HistoricalPricing;

public sealed class PriceRequirementExtractionService(
    IPriceRequirementElementService elementPricingService,
    ILogger<PriceRequirementExtractionService> logger)
    : IPriceRequirementExtractionService
{
    private const decimal MaterialReviewItemShare = 0.25m;
    private const decimal MaterialLowConfidenceEconomicShare = 0.20m;
    private const decimal PartialConfidenceCap = 0.79m;
    private const decimal ReviewConfidenceCap = 0.59m;
    private const string TechnicalFailureCode = "PRICING_TECHNICAL_FAILURE";

    private static readonly HashSet<string> CriticalWarnings = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "MEASUREMENT_AREA_MISMATCH",
        "ITEM_AMBIGUOUS",
        "GLASS_AMBIGUOUS",
        TechnicalFailureCode
    };

    public async Task<PricedRequirementExtraction> PriceAsync(
        IReadOnlyList<StructuredItemData> items,
        IReadOnlyList<ProcessingWarningData> warnings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(warnings);

        var results = new List<PricedRequirementExtractionItem>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var priced = await elementPricingService.PriceAsync(
                    item,
                    warnings,
                    cancellationToken);
                var status = HasCompleteLineRange(priced)
                    ? RequirementElementPricingStatus.Priceable
                    : RequirementElementPricingStatus.NotPriceable;
                results.Add(new PricedRequirementExtractionItem(
                    priced.ElementId,
                    priced.Reference,
                    status,
                    priced.CandidateQuery,
                    priced.TechnicalEstimate,
                    priced.CommercialEstimate,
                    priced.MappingWarnings,
                    priced.RequiresReview
                        || status == RequirementElementPricingStatus.NotPriceable));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Requirement element pricing failed. ElementId={ElementId} Reference={Reference} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}",
                    item.Sequence,
                    item.Reference,
                    exception.GetType().Name,
                    exception.Message);
                results.Add(new PricedRequirementExtractionItem(
                    item.Sequence,
                    item.Reference,
                    RequirementElementPricingStatus.TechnicalFailure,
                    null,
                    null,
                    null,
                    [TechnicalFailureCode],
                    true,
                    TechnicalFailureCode,
                    exception.Message));
            }
        }

        return Aggregate(results);
    }

    private static PricedRequirementExtraction Aggregate(
        IReadOnlyList<PricedRequirementExtractionItem> items)
    {
        var priced = items
            .Where(item => item.Status == RequirementElementPricingStatus.Priceable)
            .ToArray();
        ValidatePricingBasis(priced);
        var currency = ResolveCurrency(items);
        var notPriceableCount = items.Count - priced.Length;
        var reviewCount = items.Count(item => item.RequiresReview);
        var isPartial = notPriceableCount > 0;
        var warnings = items
            .SelectMany(item => item.MappingWarnings)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var assumptions = priced
            .SelectMany(item => item.CommercialEstimate!.Assumptions)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingData = priced
            .SelectMany(item => item.CommercialEstimate!.MissingData)
            .Concat(items
                .Where(item => item.Status != RequirementElementPricingStatus.Priceable)
                .Select(item => item.FailureCode ?? "NOT_PRICEABLE"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hasCriticalWarning = warnings.Any(warning => CriticalWarnings.Contains(warning));
        var materialReview = items.Count > 0
            && (decimal)reviewCount / items.Count >= MaterialReviewItemShare;
        var confidence = AggregateConfidence(priced);
        if (isPartial)
        {
            confidence = Math.Min(confidence, PartialConfidenceCap);
        }
        if (materialReview || HasMaterialLowConfidenceEconomicShare(priced))
        {
            confidence = Math.Min(confidence, ReviewConfidenceCap);
        }

        var requiresReview = isPartial || hasCriticalWarning || materialReview;
        return new PricedRequirementExtraction(
            items.Count,
            priced.Length,
            notPriceableCount,
            reviewCount,
            priced.Length == 0
                ? null
                : priced.Sum(item => item.LineMinimum!.Value),
            priced.Length == 0
                ? null
                : priced.Sum(item => item.LineExpected!.Value),
            priced.Length == 0
                ? null
                : priced.Sum(item => item.LineMaximum!.Value),
            currency,
            confidence,
            ConfidenceLevel(confidence),
            isPartial,
            requiresReview,
            assumptions,
            missingData,
            warnings,
            items);
    }

    private static bool HasCompleteLineRange(PricedRequirementElement estimate) =>
        estimate.LineMinimum is not null
        && estimate.LineExpected is not null
        && estimate.LineMaximum is not null;

    private static void ValidatePricingBasis(
        IEnumerable<PricedRequirementExtractionItem> items)
    {
        if (items.Any(item => item.CommercialEstimate!.PricingBasis
                != HistoricalPricingBasis.PublicQuotedItemPrices))
        {
            throw new InvalidDataException(
                "La agregacion solo admite PUBLIC_QUOTED_ITEM_PRICES.");
        }
    }

    private static string? ResolveCurrency(
        IEnumerable<PricedRequirementExtractionItem> items)
    {
        var currencies = items
            .Where(item => item.CommercialEstimate is not null)
            .Select(item => item.CommercialEstimate!.Currency)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (currencies.Length > 1)
        {
            throw new InvalidDataException(
                "No se pueden agregar estimaciones con monedas incompatibles.");
        }
        return currencies.SingleOrDefault();
    }

    private static decimal AggregateConfidence(
        IReadOnlyList<PricedRequirementExtractionItem> items)
    {
        if (items.Count == 0)
        {
            return 0m;
        }
        var totalExpected = items.Sum(item =>
            Math.Max(0m, item.LineExpected!.Value));
        if (totalExpected > 0m)
        {
            return items.Sum(item =>
                item.CommercialEstimate!.ConfidenceScore
                * Math.Max(0m, item.LineExpected!.Value))
                / totalExpected;
        }
        return items.Average(item => item.CommercialEstimate!.ConfidenceScore);
    }

    private static bool HasMaterialLowConfidenceEconomicShare(
        IReadOnlyList<PricedRequirementExtractionItem> items)
    {
        var totalExpected = items.Sum(item =>
            Math.Max(0m, item.LineExpected!.Value));
        if (totalExpected <= 0m)
        {
            return false;
        }
        var lowExpected = items
            .Where(item => item.CommercialEstimate!.ConfidenceLevel
                == HistoricalPriceConfidenceLevel.Low)
            .Sum(item => Math.Max(
                0m,
                item.LineExpected!.Value));
        return lowExpected / totalExpected >= MaterialLowConfidenceEconomicShare;
    }

    private static HistoricalPriceConfidenceLevel ConfidenceLevel(decimal score) =>
        score switch
        {
            < 0.35m => HistoricalPriceConfidenceLevel.Low,
            < 0.60m => HistoricalPriceConfidenceLevel.Medium,
            < 0.80m => HistoricalPriceConfidenceLevel.Good,
            _ => HistoricalPriceConfidenceLevel.High
        };
}
