namespace Contracts.Authentication;

public sealed record GoogleSignInResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    bool IsNewUser,
    AuthenticatedUserResponse User);
