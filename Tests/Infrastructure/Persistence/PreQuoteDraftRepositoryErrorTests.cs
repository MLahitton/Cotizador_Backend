using Application.Common.Abstractions.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class PreQuoteDraftRepositoryErrorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftQuery_TranslatesDatabaseFailure(bool tracking)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=missing;" +
                "Username=missing;Password=missing;Timeout=1")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new PreQuoteDraftRepository(context);

        var exception = await Assert.ThrowsAsync<PreQuoteDraftQueryException>(
            () => tracking
                ? repository.FindForUpdateAsync(
                    Guid.NewGuid(),
                    TestContext.Current.CancellationToken)
                : repository.FindReadAsync(
                    Guid.NewGuid(),
                    TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void PersistenceExceptions_RemainSpecific()
    {
        var databaseFailure = new InvalidOperationException();

        Assert.IsAssignableFrom<PreQuoteDraftPersistenceException>(
            new PreQuoteDraftConcurrencyException(databaseFailure));
        Assert.IsAssignableFrom<PreQuoteDraftPersistenceException>(
            new PreQuoteDraftConflictException(databaseFailure));
    }
}
