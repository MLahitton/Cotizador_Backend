namespace Application.Projects.GetProjectById;

public enum GetProjectByIdFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5
}

public sealed record ProjectByIdResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetProjectByIdResult(
    GetProjectByIdFailure Failure,
    ProjectByIdResult? Project)
{
    public bool IsSuccess => Failure == GetProjectByIdFailure.None;

    public static GetProjectByIdResult Success(
        ProjectByIdResult project)
    {
        return new GetProjectByIdResult(
            GetProjectByIdFailure.None,
            project);
    }

    public static GetProjectByIdResult Failed(
        GetProjectByIdFailure failure)
    {
        return new GetProjectByIdResult(failure, null);
    }
}
