using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Search;
using Domain.Identity;

namespace Application.Search.GetGlobalSearch;

public sealed class GetGlobalSearchService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IGlobalSearchReader globalSearchReader)
{
    private const int LimitPerCategory = 5;
    private const int MinimumSearchLength = 2;
    private const int MaximumSearchLength = 100;

    public async Task<GetGlobalSearchResult> ExecuteAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetGlobalSearchResult.Failed(
                GetGlobalSearchFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetGlobalSearchResult.Failed(
                GetGlobalSearchFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetGlobalSearchResult.Failed(
                GetGlobalSearchFailure.InactiveUser);
        }

        if (user.Role is not UserRole.User
            and not UserRole.Admin)
        {
            return GetGlobalSearchResult.Failed(
                GetGlobalSearchFailure.Forbidden);
        }

        var normalizedSearch = search?.Trim() ?? string.Empty;

        if (normalizedSearch.Length < MinimumSearchLength
            || normalizedSearch.Length > MaximumSearchLength)
        {
            return GetGlobalSearchResult.Failed(
                GetGlobalSearchFailure.InvalidRequest);
        }

        var snapshot = await globalSearchReader.SearchAsync(
            userId,
            user.Role == UserRole.Admin,
            normalizedSearch,
            LimitPerCategory,
            cancellationToken);

        return GetGlobalSearchResult.Success(snapshot);
    }
}

public enum GetGlobalSearchFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    Forbidden = 4
}

public sealed record GetGlobalSearchResult(
    GetGlobalSearchFailure Failure,
    GlobalSearchSnapshot? Search)
{
    public bool IsSuccess =>
        Failure == GetGlobalSearchFailure.None;

    public static GetGlobalSearchResult Success(
        GlobalSearchSnapshot search)
    {
        return new GetGlobalSearchResult(
            GetGlobalSearchFailure.None,
            search);
    }

    public static GetGlobalSearchResult Failed(
        GetGlobalSearchFailure failure)
    {
        return new GetGlobalSearchResult(
            failure,
            null);
    }
}