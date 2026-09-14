namespace Contracts.Administration;

public sealed record AdminUserListItemResponse(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive,
    string Role,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PreQuoteCount);

public sealed record GetAdminUsersResponse(
    IReadOnlyList<AdminUserListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);