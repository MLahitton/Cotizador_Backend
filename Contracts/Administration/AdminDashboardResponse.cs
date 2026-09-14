namespace Contracts.Administration;

public sealed record AdminDashboardResponse(
    int TotalUsers,
    int ActiveUsers,
    int UsersActiveToday,
    int UsersActiveLast7Days,
    int UsersActiveLast30Days,
    int TotalPreQuotes,
    int PreQuotesToday,
    int PreQuotesThisWeek,
    int PreQuotesThisMonth);