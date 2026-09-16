namespace Application.Common.Abstractions.Dashboard;

public interface IUserDashboardReader
{
    Task<UserDashboardSnapshot> GetDashboardAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record UserDashboardSnapshot(
    int ActiveProjects,
    int TotalPreQuotes,
    int RequirementsInProgress,
    int ProposalsRequiringReview,
    IReadOnlyList<UserDashboardRecentProjectSnapshot> RecentProjects,
    IReadOnlyList<UserDashboardAttentionItemSnapshot> AttentionItems,
    IReadOnlyList<UserDashboardActivityItemSnapshot> RecentActivity);

public sealed record UserDashboardRecentProjectSnapshot(
    Guid ProjectId,
    string Code,
    string Name,
    Guid ClientId,
    string ClientName,
    bool IsActive,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserDashboardAttentionItemSnapshot(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid PreQuoteId,
    string PreQuoteSerial,
    string? PreQuoteName,
    Guid RequirementId,
    string Type,
    string Title,
    string Description,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserDashboardActivityItemSnapshot(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid? PreQuoteId,
    string? PreQuoteSerial,
    string? PreQuoteName,
    string Type,
    string Title,
    string Description,
    DateTimeOffset OccurredAtUtc);