using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PublicExperienceSubmissionPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [Fact]
    public async Task ConcurrentRealRepositories_OneRow_Jsonb_NoCommercialEntities()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var token = TestContext.Current.CancellationToken;
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var storage = new IntakeStorage();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task Barrier(CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        var first = new PublicExperienceIntakeService(new SynchronizedInsert(new PublicExperienceSubmissionRepository(firstContext), Barrier),
            storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
        var second = new PublicExperienceIntakeService(new SynchronizedInsert(new PublicExperienceSubmissionRepository(secondContext), Barrier),
            storage, TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
        var file = PublicExperienceIntakeTests.Upload();
        var payload = PublicExperienceIntakeTests.Payload([file]);
        var results = await Task.WhenAll(first.ExecuteAsync(payload, [file], token),
            second.ExecuteAsync(payload, [file], token));
        Assert.Contains(results, x => x.StatusCode == 201);
        Assert.Contains(results, x => x.StatusCode == 200);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(1, await read.Set<PublicExperienceSubmission>().CountAsync(token));
        Assert.Equal(1, await read.Set<PublicExperienceSubmissionFile>().CountAsync(token));
        Assert.Equal(2, storage.Files.Count);
        Assert.Equal(0, storage.Deletes);
        var savedFile = await read.Set<PublicExperienceSubmissionFile>().SingleAsync(token);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, storage.Files[savedFile.StorageKey]);
        Assert.False(await read.Clients.AnyAsync(token));
        Assert.False(await read.Projects.AnyAsync(token));
        Assert.False(await read.PreQuotes.AnyAsync(token));
        Assert.False(await read.Requirements.AnyAsync(token));
        var saved = await read.Set<PublicExperienceSubmission>().SingleAsync(token);
        Assert.Contains("yes", saved.AnswersJson);
        Assert.Equal("AD01", saved.AdvisorCode);
    }

    private sealed class SynchronizedInsert(IPublicExperienceSubmissionRepository inner, Func<CancellationToken, Task> barrier)
        : IPublicExperienceSubmissionRepository
    {
        public Task<PublicExperienceSubmission?> FindAsync(string id, CancellationToken ct) => inner.FindAsync(id, ct);
        public async Task<bool> TryInsertAsync(PublicExperienceSubmission value, CancellationToken ct)
        {
            await barrier(ct);
            return await inner.TryInsertAsync(value, ct);
        }
    }
}
