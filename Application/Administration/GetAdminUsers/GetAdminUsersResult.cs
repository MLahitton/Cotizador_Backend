using Domain.Identity;

namespace Application.Administration.GetAdminUsers;

public enum GetAdminUsersFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    Forbidden = 4,
    QueryError = 5
}

public sealed record AdminUserListItemResult(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive,
    UserRole Role,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PreQuoteCount);

public sealed record AdminUsersPageResult(
    IReadOnlyList<AdminUserListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetAdminUsersResult(
    GetAdminUsersFailure Failure,
    AdminUsersPageResult? Page)
{
    public bool IsSuccess =>
        Failure == GetAdminUsersFailure.None;

    public static GetAdminUsersResult Success(
        AdminUsersPageResult page)
        => new(
            GetAdminUsersFailure.None,
            page);

    public static GetAdminUsersResult Failed(
        GetAdminUsersFailure failure)
        => new(
            failure,
            null);
}