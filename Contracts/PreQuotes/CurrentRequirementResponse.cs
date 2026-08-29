namespace Contracts.PreQuotes;

public sealed record CurrentRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    string Status,
    string? CommercialLine,
    DateTimeOffset CreatedAtUtc,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    Guid? LatestAttemptId,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    IReadOnlyList<RequirementDocumentResponse> Documents);
