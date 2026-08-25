using Application.Common.Abstractions.Catalogs;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.HistoricalPricing;

public sealed record TechnicalProposalItemHistoricalPricingMapping(
    HistoricalCandidateQuery CandidateQuery,
    decimal Quantity,
    decimal? PricingArea,
    bool RequiresReview,
    IReadOnlyList<string> MappingWarnings);

public interface ITechnicalProposalItemToHistoricalPricingMapper
{
    TechnicalProposalItemHistoricalPricingMapping Map(
        RequirementTechnicalProposalItem proposalItem,
        ProductSystemCatalogReadModel system,
        GlassTypeCatalogReadModel glass,
        FinishTypeCatalogReadModel finish);
}

public enum TechnicalProposalPricingItemStatus
{
    Priceable = 1,
    NotPriceable = 2,
    NoEstimate = 3,
    TechnicalFailure = 4
}

public sealed record TechnicalProposalPricingMoneyRange(
    decimal? Minimum,
    decimal? Expected,
    decimal? Maximum);

public sealed record TechnicalProposalPricingComparableReadModel(
    string CandidateId,
    string? HistoricalReference,
    decimal PublicUnitPrice,
    decimal ProjectedPrice,
    decimal BackendScore,
    decimal? Ai2Similarity,
    string? SimilarityLevel,
    decimal FinalWeight);

public sealed record TechnicalProposalPricingItemReadModel(
    Guid ProposalItemId,
    Guid ExtractedItemId,
    string? ElementId,
    int Sequence,
    string? Reference,
    string Description,
    string Status,
    string ConfigurationSource,
    decimal? Quantity,
    decimal? PricingAreaM2,
    TechnicalProposalPricingMoneyRange Unit,
    TechnicalProposalPricingMoneyRange Line,
    decimal? ConfidenceScore,
    string? ConfidenceLevel,
    bool RequiresReview,
    IReadOnlyList<string> MappingWarnings,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<TechnicalProposalPricingComparableReadModel> Comparables);

public sealed record RequirementTechnicalProposalPricingReadModel(
    Guid RequirementId,
    Guid TechnicalProposalId,
    string Currency,
    string PricingBasis,
    int ItemCount,
    int PricedItemCount,
    int NotPriceableItemCount,
    int ItemsRequiringReview,
    TechnicalProposalPricingMoneyRange EstimatedSubtotal,
    bool IsCompleteTotal,
    bool RequiresReview,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<TechnicalProposalPricingItemReadModel> Items);
