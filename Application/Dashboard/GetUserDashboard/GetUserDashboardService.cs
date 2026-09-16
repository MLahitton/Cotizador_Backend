using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Dashboard;
using Domain.Identity;

namespace Application.Dashboard.GetUserDashboard;

public sealed class GetUserDashboardService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IUserDashboardReader dashboardReader)
{
    public async Task<GetUserDashboardResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetUserDashboardResult.Failed(
                GetUserDashboardFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetUserDashboardResult.Failed(
                GetUserDashboardFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetUserDashboardResult.Failed(
                GetUserDashboardFailure.InactiveUser);
        }

        if (user.Role != UserRole.User)
        {
            return GetUserDashboardResult.Failed(
                GetUserDashboardFailure.Forbidden);
        }

        var snapshot = await dashboardReader.GetDashboardAsync(
            userId,
            cancellationToken);

        return GetUserDashboardResult.Success(
            snapshot.ActiveProjects,
            snapshot.TotalPreQuotes,
            snapshot.RequirementsInProgress,
            snapshot.ProposalsRequiringReview,
            snapshot.RecentProjects,
            snapshot.AttentionItems,
            snapshot.RecentActivity);
    }
}

public enum GetUserDashboardFailure
{
    None = 0,
    Unauthorized = 1,
    InactiveUser = 2,
    Forbidden = 3
}

public sealed record GetUserDashboardResult(
    GetUserDashboardFailure Failure,
    int ActiveProjects,
    int TotalPreQuotes,
    int RequirementsInProgress,
    int ProposalsRequiringReview,
    IReadOnlyList<UserDashboardRecentProjectSnapshot> RecentProjects,
    IReadOnlyList<UserDashboardAttentionItemSnapshot> AttentionItems,
    IReadOnlyList<UserDashboardActivityItemSnapshot> RecentActivity)
{
    public bool IsSuccess =>
        Failure == GetUserDashboardFailure.None;

    public static GetUserDashboardResult Success(
        int activeProjects,
        int totalPreQuotes,
        int requirementsInProgress,
        int proposalsRequiringReview,
        IReadOnlyList<UserDashboardRecentProjectSnapshot> recentProjects,
        IReadOnlyList<UserDashboardAttentionItemSnapshot> attentionItems,
        IReadOnlyList<UserDashboardActivityItemSnapshot> recentActivity)
    {
        return new GetUserDashboardResult(
            GetUserDashboardFailure.None,
            activeProjects,
            totalPreQuotes,
            requirementsInProgress,
            proposalsRequiringReview,
            recentProjects,
            attentionItems,
            recentActivity);
    }

    public static GetUserDashboardResult Failed(
        GetUserDashboardFailure failure)
    {
        return new GetUserDashboardResult(
            failure,
            0,
            0,
            0,
            0,
            [],
            [],
            []);
    }
}