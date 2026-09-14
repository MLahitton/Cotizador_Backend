namespace Application.Common.Abstractions.Administration;

public interface IAdministrationDashboardReader
{
    Task<AdministrationDashboardSnapshot> GetDashboardAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed record AdministrationDashboardSnapshot(
    int TotalUsers,
    int ActiveUsers,
    int UsersActiveToday,
    int UsersActiveLast7Days,
    int UsersActiveLast30Days,
    int TotalPreQuotes,
    int PreQuotesToday,
    int PreQuotesThisWeek,
    int PreQuotesThisMonth);