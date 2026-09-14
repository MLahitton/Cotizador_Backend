namespace Contracts.Administration;

public sealed record AdminPreQuoteUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName);

public sealed record AdminPreQuoteProjectResponse(
    Guid Id,
    string Code,
    string Name);

public sealed record AdminPreQuoteListItemResponse(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AdminPreQuoteUserResponse CreatedBy,
    AdminPreQuoteProjectResponse Project,
    bool HasRequirement,
    Guid? LatestRequirementId,
    string? LatestRequirementStatus,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    int? TechnicalProposalItemCount,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);

public sealed record GetAdminPreQuotesResponse(
    IReadOnlyList<AdminPreQuoteListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);