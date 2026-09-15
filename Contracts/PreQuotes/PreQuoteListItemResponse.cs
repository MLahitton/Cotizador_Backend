namespace Contracts.PreQuotes;

public sealed record PreQuoteCreatedByResponse(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName);

public sealed record PreQuoteListItemResponse(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PreQuoteCreatedByResponse CreatedBy,
    bool HasRequirement,
    Guid? LatestRequirementId,
    string? LatestRequirementStatus,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    int? TechnicalProposalItemCount,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);
