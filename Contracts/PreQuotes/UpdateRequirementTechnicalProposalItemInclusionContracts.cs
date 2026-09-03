namespace Contracts.PreQuotes;

public sealed record UpdateRequirementTechnicalProposalItemInclusionRequest(
    bool IsIncluded,
    string? Reason = null);

public sealed record UpdateRequirementTechnicalProposalItemInclusionResponse(
    Guid TechnicalProposalId,
    Guid ItemId,
    bool IsIncluded,
    DateTimeOffset? ExcludedAtUtc,
    Guid? ExcludedByUserId,
    string? ExclusionReason,
    long CommercialRevision);
