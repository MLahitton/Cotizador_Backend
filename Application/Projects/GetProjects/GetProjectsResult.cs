using Domain.Clients;

namespace Application.Projects.GetProjects;

public enum GetProjectsFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    QueryError = 4
}

public sealed record ProjectClientSummaryResult(
    Guid Id,
    ClientType ClientType,
    string LegalName,
    string? TradeName,
    ClientDocumentType? DocumentType,
    string? DocumentNumber);

public sealed record AdministrativeProjectListItemResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ProjectClientSummaryResult Client);

public sealed record ProjectsPageResult(
    IReadOnlyList<AdministrativeProjectListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetProjectsResult(
    GetProjectsFailure Failure,
    ProjectsPageResult? Page)
{
    public bool IsSuccess => Failure == GetProjectsFailure.None;

    public static GetProjectsResult Success(ProjectsPageResult page)
        => new(GetProjectsFailure.None, page);

    public static GetProjectsResult Failed(GetProjectsFailure failure)
        => new(failure, null);
}
