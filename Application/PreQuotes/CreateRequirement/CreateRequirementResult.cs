namespace Application.PreQuotes.CreateRequirement;

public enum CreateRequirementFailure
{
    None = 0,
    InvalidRequest = 1,
    InvalidFileName = 2,
    UnsupportedFileType = 3,
    EmptyFile = 4,
    FileTooLarge = 5,
    TooManyFiles = 6,
    Unauthorized = 7,
    InactiveUser = 8,
    PreQuoteNotFound = 9,
    ProjectNotFound = 10,
    InactiveProject = 11,
    ClientNotFound = 12,
    InactiveClient = 13,
    QueryError = 14,
    StorageError = 15,
    PersistenceError = 16
}

public sealed record CreatedRequirementResult(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateRequirementResult(
    bool IsSuccess,
    CreatedRequirementResult? Requirement,
    CreateRequirementFailure Failure)
{
    public static CreateRequirementResult Success(
        CreatedRequirementResult requirement)
    {
        return new CreateRequirementResult(
            true,
            requirement,
            CreateRequirementFailure.None);
    }

    public static CreateRequirementResult Failed(
        CreateRequirementFailure failure)
    {
        return new CreateRequirementResult(false, null, failure);
    }
}
