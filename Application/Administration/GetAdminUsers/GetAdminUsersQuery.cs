namespace Application.Administration.GetAdminUsers;

public sealed record GetAdminUsersQuery(
    string? Search,
    string? Status,
    string? Role,
    int Page,
    int PageSize);