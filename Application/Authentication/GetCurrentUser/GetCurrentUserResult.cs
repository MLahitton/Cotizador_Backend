namespace Application.Authentication.GetCurrentUser;

public enum GetCurrentUserFailure
{
    None = 0,
    Unauthorized = 1,
    InactiveUser = 2
}

public sealed record GetCurrentUserResult(
    GetCurrentUserFailure Failure,
    Guid? Id,
    string? Email,
    string? FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive)
{
    public bool IsSuccess => Failure == GetCurrentUserFailure.None;

    public static GetCurrentUserResult Success(
        Guid id,
        string email,
        string firstName,
        string? lastName,
        string? profilePictureUrl,
        bool isActive)
    {
        return new GetCurrentUserResult(
            GetCurrentUserFailure.None,
            id,
            email,
            firstName,
            lastName,
            profilePictureUrl,
            isActive);
    }

    public static GetCurrentUserResult Failed(GetCurrentUserFailure failure)
    {
        return new GetCurrentUserResult(
            failure,
            null,
            null,
            null,
            null,
            null,
            false);
    }
}
