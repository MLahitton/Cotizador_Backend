namespace Application.Projects.GetClientProjects;

public enum GetClientProjectsFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    ClientNotFound = 4,
    InactiveClient = 5,
    QueryError = 6
}

public sealed record ProjectListItemResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ClientProjectsPageResult(
    IReadOnlyList<ProjectListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetClientProjectsResult(
    GetClientProjectsFailure Failure,
    ClientProjectsPageResult? Page)
{
    public bool IsSuccess =>
        Failure == GetClientProjectsFailure.None;

    public static GetClientProjectsResult Success(
        ClientProjectsPageResult page)
    {
        return new GetClientProjectsResult(
            GetClientProjectsFailure.None,
            page);
    }

    public static GetClientProjectsResult Failed(
        GetClientProjectsFailure failure)
    {
        return new GetClientProjectsResult(failure, null);
    }
}
