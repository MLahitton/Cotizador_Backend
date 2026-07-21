namespace Contracts.Authentication;

public sealed record AuthenticatedUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive);
