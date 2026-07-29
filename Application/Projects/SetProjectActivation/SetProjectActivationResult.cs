namespace Application.Projects.SetProjectActivation;

public enum SetProjectActivationFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5,
    PersistenceError = 6
}

public sealed record ProjectActivationResult(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SetProjectActivationResult(
    SetProjectActivationFailure Failure,
    ProjectActivationResult? Project)
{
    public bool IsSuccess =>
        Failure == SetProjectActivationFailure.None;

    public static SetProjectActivationResult Success(
        ProjectActivationResult project)
        => new(SetProjectActivationFailure.None, project);

    public static SetProjectActivationResult Failed(
        SetProjectActivationFailure failure)
        => new(failure, null);
}
