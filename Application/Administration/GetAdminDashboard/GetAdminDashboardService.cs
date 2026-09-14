using Application.Common.Abstractions.Administration;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;

namespace Application.Administration.GetAdminDashboard;

public sealed class GetAdminDashboardService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IAdministrationDashboardReader dashboardReader,
    TimeProvider timeProvider)
{
    public async Task<GetAdminDashboardResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated ||
            currentUser.UserId is not Guid userId)
        {
            return GetAdminDashboardResult.Failed(
                GetAdminDashboardFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetAdminDashboardResult.Failed(
                GetAdminDashboardFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetAdminDashboardResult.Failed(
                GetAdminDashboardFailure.InactiveUser);
        }

        if (user.Role != UserRole.Admin)
        {
            return GetAdminDashboardResult.Failed(
                GetAdminDashboardFailure.Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow();

        var snapshot = await dashboardReader.GetDashboardAsync(
            nowUtc,
            cancellationToken);

        return GetAdminDashboardResult.Success(
            snapshot.TotalUsers,
            snapshot.ActiveUsers,
            snapshot.UsersActiveToday,
            snapshot.UsersActiveLast7Days,
            snapshot.UsersActiveLast30Days,
            snapshot.TotalPreQuotes,
            snapshot.PreQuotesToday,
            snapshot.PreQuotesThisWeek,
            snapshot.PreQuotesThisMonth);
    }
}

public enum GetAdminDashboardFailure
{
    None = 0,
    Unauthorized = 1,
    InactiveUser = 2,
    Forbidden = 3
}

public sealed record GetAdminDashboardResult(
    GetAdminDashboardFailure Failure,
    int TotalUsers,
    int ActiveUsers,
    int UsersActiveToday,
    int UsersActiveLast7Days,
    int UsersActiveLast30Days,
    int TotalPreQuotes,
    int PreQuotesToday,
    int PreQuotesThisWeek,
    int PreQuotesThisMonth)
{
    public bool IsSuccess =>
        Failure == GetAdminDashboardFailure.None;

    public static GetAdminDashboardResult Success(
        int totalUsers,
        int activeUsers,
        int usersActiveToday,
        int usersActiveLast7Days,
        int usersActiveLast30Days,
        int totalPreQuotes,
        int preQuotesToday,
        int preQuotesThisWeek,
        int preQuotesThisMonth)
    {
        return new GetAdminDashboardResult(
            GetAdminDashboardFailure.None,
            totalUsers,
            activeUsers,
            usersActiveToday,
            usersActiveLast7Days,
            usersActiveLast30Days,
            totalPreQuotes,
            preQuotesToday,
            preQuotesThisWeek,
            preQuotesThisMonth);
    }

    public static GetAdminDashboardResult Failed(
        GetAdminDashboardFailure failure)
    {
        return new GetAdminDashboardResult(
            failure,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
    }
}