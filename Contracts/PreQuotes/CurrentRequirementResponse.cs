namespace Contracts.PreQuotes;

public sealed record CurrentRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    string Status,
    string? CommercialLine,
    DateTimeOffset CreatedAtUtc,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);
