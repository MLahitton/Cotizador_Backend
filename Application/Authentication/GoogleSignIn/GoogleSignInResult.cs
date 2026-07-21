namespace Application.Authentication.GoogleSignIn;

public enum GoogleSignInFailure
{
    None = 0,
    InvalidRequest = 1,
    InvalidGoogleToken = 2,
    InactiveUser = 3,
    IdentityConflict = 4,
    PersistenceError = 5,
    ProviderUnavailable = 6
}

public sealed record GoogleSignInUserResult(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive);

public sealed record GoogleSignInResult(
    GoogleSignInFailure Failure,
    string? AccessToken,
    DateTimeOffset? ExpiresAtUtc,
    bool IsNewUser,
    GoogleSignInUserResult? User)
{
    public bool IsSuccess => Failure == GoogleSignInFailure.None;

    public static GoogleSignInResult Success(
        string accessToken,
        DateTimeOffset expiresAtUtc,
        bool isNewUser,
        GoogleSignInUserResult user)
    {
        return new GoogleSignInResult(
            GoogleSignInFailure.None,
            accessToken,
            expiresAtUtc,
            isNewUser,
            user);
    }

    public static GoogleSignInResult Failed(GoogleSignInFailure failure)
    {
        return new GoogleSignInResult(
            failure,
            null,
            null,
            false,
            null);
    }
}
