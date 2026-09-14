namespace Application.Administration.GetAdminPreQuotes;

public enum GetAdminPreQuotesFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    Forbidden = 4,
    QueryError = 5
}

public sealed record AdminPreQuoteUserResult(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName);

public sealed record AdminPreQuoteProjectResult(
    Guid Id,
    string Code,
    string Name);

public sealed record AdminPreQuoteListItemResult(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AdminPreQuoteUserResult CreatedBy,
    AdminPreQuoteProjectResult Project,
    bool HasRequirement,
    Guid? LatestRequirementId,
    string? LatestRequirementStatus,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    int? TechnicalProposalItemCount,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);

public sealed record AdminPreQuotesPageResult(
    IReadOnlyList<AdminPreQuoteListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetAdminPreQuotesResult(
    GetAdminPreQuotesFailure Failure,
    AdminPreQuotesPageResult? Page)
{
    public bool IsSuccess =>
        Failure == GetAdminPreQuotesFailure.None;

    public static GetAdminPreQuotesResult Success(
        AdminPreQuotesPageResult page)
        => new(
            GetAdminPreQuotesFailure.None,
            page);

    public static GetAdminPreQuotesResult Failed(
        GetAdminPreQuotesFailure failure)
        => new(
            failure,
            null);
}