namespace Application.Projects.UpdateProject;

public enum UpdateProjectFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    DuplicateCode = 5,
    QueryError = 6,
    PersistenceError = 7
}

public sealed record UpdatedProjectResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdateProjectResult(
    UpdateProjectFailure Failure,
    UpdatedProjectResult? Project)
{
    public bool IsSuccess => Failure == UpdateProjectFailure.None;

    public static UpdateProjectResult Success(
        UpdatedProjectResult project)
    {
        return new UpdateProjectResult(
            UpdateProjectFailure.None,
            project);
    }

    public static UpdateProjectResult Failed(
        UpdateProjectFailure failure)
    {
        return new UpdateProjectResult(failure, null);
    }
}
