using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;

namespace Application.HistoricalPricing;

public sealed class PriceRequirementElementService(
    IRequirementElementToHistoricalPricingMapper mapper,
    IHistoricalTechnicalPriceEstimator technicalEstimator,
    IHistoricalCommercialPriceEstimator commercialEstimator)
    : IPriceRequirementElementService
{
    public async Task<PricedRequirementElement> PriceAsync(
        StructuredItemData item,
        IReadOnlyList<ProcessingWarningData> warnings,
        CancellationToken cancellationToken = default)
    {
        var mapping = mapper.Map(item, warnings);
        var technical = await technicalEstimator.EstimateAsync(
            mapping.CandidateQuery,
            cancellationToken);
        var commercial = commercialEstimator.FromTechnical(technical);
        return new PricedRequirementElement(
            mapping.ElementId,
            mapping.Reference,
            mapping.CandidateQuery,
            technical,
            commercial,
            mapping.MappingWarnings,
            mapping.RequiresReview || technical.RequiresReview);
    }
}
