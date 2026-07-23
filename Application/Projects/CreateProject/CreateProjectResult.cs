namespace Application.Projects.CreateProject;

public enum CreateProjectFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    ClientNotFound = 4,
    InactiveClient = 5,
    DuplicateCode = 6,
    PersistenceError = 7
}

public sealed record CreatedProjectResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateProjectResult(
    CreateProjectFailure Failure,
    CreatedProjectResult? Project)
{
    public bool IsSuccess =>
        Failure == CreateProjectFailure.None;

    public static CreateProjectResult Success(
        CreatedProjectResult project)
    {
        return new CreateProjectResult(
            CreateProjectFailure.None,
            project);
    }

    public static CreateProjectResult Failed(
        CreateProjectFailure failure)
    {
        return new CreateProjectResult(failure, null);
    }
}
