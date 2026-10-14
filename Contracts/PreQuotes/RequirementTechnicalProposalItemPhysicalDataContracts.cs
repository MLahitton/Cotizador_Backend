namespace Contracts.PreQuotes;

// Null or omitted fields preserve their current effective value.
public sealed record UpdateRequirementTechnicalProposalItemPhysicalDataRequest(
    int? Quantity,
    int? WidthMm,
    int? HeightMm);

public sealed record UpdateRequirementTechnicalProposalItemPhysicalDataResponse(
    Guid TechnicalProposalId,
    Guid ItemId,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    long CommercialRevision,
    RequirementTechnicalProposalItemDataCompletenessResponse DataCompleteness);
