namespace Contracts.PreQuotes;

public sealed record CurrentRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);
