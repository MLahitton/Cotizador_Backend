namespace Contracts.Dashboard;

public sealed record UserDashboardResponse(
    int ActiveProjects,
    int TotalPreQuotes,
    int RequirementsInProgress,
    int ProposalsRequiringReview,
    IReadOnlyList<UserDashboardRecentProjectResponse> RecentProjects,
    IReadOnlyList<UserDashboardAttentionItemResponse> AttentionItems,
    IReadOnlyList<UserDashboardActivityItemResponse> RecentActivity);

public sealed record UserDashboardRecentProjectResponse(
    Guid ProjectId,
    string Code,
    string Name,
    Guid ClientId,
    string ClientName,
    bool IsActive,
    DateTimeOffset UpdatedAtUtc);

public sealed record UserDashboardAttentionItemResponse(
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

public sealed record UserDashboardActivityItemResponse(
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