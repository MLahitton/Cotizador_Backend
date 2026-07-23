namespace Application.PreQuotes.GetProjectPreQuotes;

public enum GetProjectPreQuotesFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    ProjectNotFound = 4,
    QueryError = 5
}

public sealed record PreQuoteListItemResult(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectPreQuotesPageResult(
    IReadOnlyList<PreQuoteListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetProjectPreQuotesResult(
    GetProjectPreQuotesFailure Failure,
    ProjectPreQuotesPageResult? Page)
{
    public bool IsSuccess =>
        Failure == GetProjectPreQuotesFailure.None;

    public static GetProjectPreQuotesResult Success(
        ProjectPreQuotesPageResult page)
    {
        return new GetProjectPreQuotesResult(
            GetProjectPreQuotesFailure.None,
            page);
    }

    public static GetProjectPreQuotesResult Failed(
        GetProjectPreQuotesFailure failure)
    {
        return new GetProjectPreQuotesResult(failure, null);
    }
}
