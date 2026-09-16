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
        var colombiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "SA Pacific Standard Time"
                : "America/Bogota");

        var nowColombia = TimeZoneInfo.ConvertTime(
            nowUtc,
            colombiaTimeZone);

        var startOfTodayColombia = new DateTimeOffset(
            nowColombia.Year,
            nowColombia.Month,
            nowColombia.Day,
            0,
            0,
            0,
            nowColombia.Offset);

        var startOfTomorrowColombia =
            startOfTodayColombia.AddDays(1);

        var startOfLast7DaysColombia =
            startOfTodayColombia.AddDays(-6);

        var startOfLast30DaysColombia =
            startOfTodayColombia.AddDays(-29);

        var daysSinceMonday =
            ((int)startOfTodayColombia.DayOfWeek + 6) % 7;

        var startOfWeekColombia =
            startOfTodayColombia.AddDays(-daysSinceMonday);

        var startOfMonthColombia = new DateTimeOffset(
            nowColombia.Year,
            nowColombia.Month,
            1,
            0,
            0,
            0,
            nowColombia.Offset);

        var startOfTodayUtc =
            startOfTodayColombia.ToUniversalTime();

        var startOfTomorrowUtc =
            startOfTomorrowColombia.ToUniversalTime();

        var startOfLast7DaysUtc =
            startOfLast7DaysColombia.ToUniversalTime();

        var startOfLast30DaysUtc =
            startOfLast30DaysColombia.ToUniversalTime();

        var startOfWeekUtc =
            startOfWeekColombia.ToUniversalTime();

        var startOfMonthUtc =
            startOfMonthColombia.ToUniversalTime();

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
                        user.LastLoginAtUtc >= startOfTodayUtc &&
                        user.LastLoginAtUtc < startOfTomorrowUtc),

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
                        preQuote.CreatedAtUtc >= startOfTodayUtc &&
                        preQuote.CreatedAtUtc < startOfTomorrowUtc),

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