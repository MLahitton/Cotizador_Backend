using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PreQuoteSerialRepositoryTests(PostgreSqlIntegrationFixture fixture)
{
    [Fact]
    public async Task ReserveNextSerialAsync_ReturnsFirstSerialForYear()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new PreQuoteRepository(context);

        var serial = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Equal("PC-2026-0001", serial);
    }

    [Fact]
    public async Task ReserveNextSerialAsync_IncrementsWithinYearAndResetsByYear()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new PreQuoteRepository(context);

        var first = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        var second = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        var nextYear = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Equal("PC-2026-0001", first);
        Assert.Equal("PC-2026-0002", second);
        Assert.Equal("PC-2027-0001", nextYear);
    }
    [Fact]
    public async Task ReserveNextSerialAsync_WithExistingCounterUsesNextAvailableSequence()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO core.pre_quote_serial_counters (year, next_sequence)
            VALUES (2026, 54);
            """,
            TestContext.Current.CancellationToken);
        var repository = new PreQuoteRepository(context);

        var first = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        var second = await repository.ReserveNextSerialAsync(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        var counter = await context.PreQuoteSerialCounters
            .AsNoTracking()
            .SingleAsync(
                item => item.Year == 2026,
                TestContext.Current.CancellationToken);

        Assert.Equal("PC-2026-0054", first);
        Assert.Equal("PC-2026-0055", second);
        Assert.Equal(56, counter.NextSequence);
    }


    [Fact]
    public async Task ReserveNextSerialAsync_IsSafeForConcurrentReservations()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var serials = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = fixture.CreateDbContext();
            var repository = new PreQuoteRepository(context);
            return await repository.ReserveNextSerialAsync(
                at,
                TestContext.Current.CancellationToken);
        }));

        Assert.Equal(20, serials.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("PC-2026-0001", serials);
        Assert.Contains("PC-2026-0020", serials);
    }
}