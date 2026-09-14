using Application.Common.Abstractions.Administration;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Queries;

public sealed class AdministrationDashboardReader(
    ApplicationDbContext dbContext)
    : IAdministrationDashboardReader
{
    public async Task<AdministrationDashboardSnapshot> GetDashboardAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var startOfTodayUtc = new DateTimeOffset(
            nowUtc.UtcDateTime.Date,
            TimeSpan.Zero);

        var startOfLast7DaysUtc =
            startOfTodayUtc.AddDays(-6);

        var startOfLast30DaysUtc =
            startOfTodayUtc.AddDays(-29);

        var daysSinceMonday =
            ((int)startOfTodayUtc.DayOfWeek + 6) % 7;

        var startOfWeekUtc =
            startOfTodayUtc.AddDays(-daysSinceMonday);

        var startOfMonthUtc = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        var userMetrics = await dbContext.Users
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalUsers = group.Count(),

                ActiveUsers = group.Count(
                    user => user.IsActive),

                UsersActiveToday = group.Count(
                    user =>
                        user.LastLoginAtUtc != null &&
                        user.LastLoginAtUtc >= startOfTodayUtc),

                UsersActiveLast7Days = group.Count(
                    user =>
                        user.LastLoginAtUtc != null &&
                        user.LastLoginAtUtc >= startOfLast7DaysUtc),

                UsersActiveLast30Days = group.Count(
                    user =>
                        user.LastLoginAtUtc != null &&
                        user.LastLoginAtUtc >= startOfLast30DaysUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var preQuoteMetrics = await dbContext.PreQuotes
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalPreQuotes = group.Count(),

                PreQuotesToday = group.Count(
                    preQuote =>
                        preQuote.CreatedAtUtc >= startOfTodayUtc),

                PreQuotesThisWeek = group.Count(
                    preQuote =>
                        preQuote.CreatedAtUtc >= startOfWeekUtc),

                PreQuotesThisMonth = group.Count(
                    preQuote =>
                        preQuote.CreatedAtUtc >= startOfMonthUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new AdministrationDashboardSnapshot(
            userMetrics?.TotalUsers ?? 0,
            userMetrics?.ActiveUsers ?? 0,
            userMetrics?.UsersActiveToday ?? 0,
            userMetrics?.UsersActiveLast7Days ?? 0,
            userMetrics?.UsersActiveLast30Days ?? 0,
            preQuoteMetrics?.TotalPreQuotes ?? 0,
            preQuoteMetrics?.PreQuotesToday ?? 0,
            preQuoteMetrics?.PreQuotesThisWeek ?? 0,
            preQuoteMetrics?.PreQuotesThisMonth ?? 0);
    }
}