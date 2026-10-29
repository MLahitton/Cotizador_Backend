using Application.PreQuotes.ProcessRequirement;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobPublicationConcurrencyPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private JobPublicationTestSupport H => new(fixture);

    [Theory]
    [InlineData("same")] [InlineData("different")] [InlineData("cancel-first")] [InlineData("publish-first")]
    public async Task OrderedRaces_IndependentPids_OneWinner(string scenario)
    {
        var s = await H.Create();
        var guarded = Signal(); var release = Signal(); var competing = Signal();
        var pid1 = 0; var pid2 = 0;
        var hook1 = new CommandHook(async command =>
        {
            pid1 = ((NpgsqlConnection)command.Connection!).ProcessID;
            guarded.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        });
        var hook2 = new CommandHook(beforeQuoteLock: command =>
        {
            pid2 = ((NpgsqlConnection)command.Connection!).ProcessID;
            competing.TrySetResult();
        });
        Task<JobPublicationResult>? firstPublish = null, secondPublish = null;
        Task<JobTerminationResult>? cancel = null;
        if (scenario == "cancel-first") cancel = H.H.Call("request", s.Execution, H.H.Service(hook1));
        else firstPublish = H.Publish(s, H.Service(hook1));
        await guarded.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        try
        {
            if (scenario == "publish-first") cancel = H.H.Call("request", s.Execution, H.H.Service(hook2));
            else secondPublish = H.Publish(scenario == "different" ? JobPublicationTestSupport.Different(s) : s, H.Service(hook2));
            await competing.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(0, pid1);
            Assert.NotEqual(0, pid2);
            Assert.NotEqual(pid1, pid2);
        }
        finally { release.TrySetResult(); }
        if (firstPublish is not null) Assert.Equal(JobPublicationState.Published, (await firstPublish).State);
        if (secondPublish is not null)
        {
            var result = await secondPublish;
            Assert.Equal(scenario == "same" ? JobPublicationState.AlreadyPublished : JobPublicationState.Conflict, result.State);
            if (scenario == "different") Assert.Equal(JobPublicationFailure.ResultMismatch, result.Failure);
            if (scenario == "cancel-first") Assert.Equal(JobPublicationFailure.CancellationRequested, result.Failure);
        }
        if (cancel is not null)
            Assert.Equal(scenario == "cancel-first" ? JobTerminationState.CancellationRequested : JobTerminationState.Conflict, (await cancel).State);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(scenario == "cancel-first" ? 0 : 1, await db.RequirementExtractionResults.CountAsync(Token));
        Assert.Equal(scenario == "cancel-first" ? 0 : 1, await db.RequirementTechnicalProposals.CountAsync(Token));
        Assert.Equal(1, await db.RequirementProcessingAttempts.CountAsync(Token));
        if (scenario != "cancel-first") Assert.Equal(s.Snapshot.ContentHash, (await db.RequirementProcessingJobs.SingleAsync(Token)).PublicationContentHash);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AfterRelatedWrites_ErrorOrExpiredLease_RollsBackEveryRow(bool expire)
    {
        var s = await H.Create();
        var expiry = (await H.H.Now()).AddSeconds(5);
        if (expire)
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {expiry}", Token);
        }
        var before = await H.Snapshot();
        var saved = Signal(); var release = Signal();
        var pending = H.Publish(s, H.Service(new Saved(async db =>
        {
            Assert.True(await db.Set<global::Domain.PreQuotes.RequirementExtractionResult>().AnyAsync(Token));
            if (!expire) throw new IOException("Injected failure after graph writes");
            Assert.True(await H.H.Now() < expiry);
            saved.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        })));
        if (expire)
        {
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                while (await H.H.Now() <= expiry) await Task.Delay(25, deadline.Token);
            }
            finally { release.TrySetResult(); }
        }
        var result = await pending;
        Assert.Equal(expire ? JobPublicationState.Conflict : JobPublicationState.PersistenceError, result.State);
        if (expire) Assert.Equal(JobPublicationFailure.LeaseConflict, result.Failure);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LostResponseAfterRealCommit_ReconcilesOrReturnsIndeterminate(bool unavailable)
    {
        var s = await H.Create();
        var committed = false;
        var result = await H.Publish(s, H.Service(new CommitHook(() =>
            { committed = true; throw new IOException("Injected lost commit response"); }),
            new OpenHook(() => { if (committed && unavailable) throw new IOException("Injected observation outage"); })));
        Assert.True(committed);
        Assert.Equal(unavailable ? JobPublicationState.Indeterminate : JobPublicationState.AlreadyPublished, result.State);
        var before = await H.Snapshot();
        Assert.Equal(JobPublicationState.AlreadyPublished, (await H.Publish(s)).State);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData("save", false)] [InlineData("before-commit", false)]
    [InlineData("after-commit", false)] [InlineData("after-commit", true)]
    public async Task CallerCancellation_DoesNotCancelBusinessJob(string boundary, bool unavailable)
    {
        var s = await H.Create();
        var before = await H.Snapshot();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reached = false;
        void Cancel() { reached = true; caller.Cancel(); throw new OperationCanceledException(caller.Token); }
        var service = boundary == "save" ? H.Service(new Saved(_ => { Cancel(); return Task.CompletedTask; }))
            : H.Service(new CommitHook(Cancel, after: boundary == "after-commit"),
                new OpenHook(() => { if (reached && unavailable) throw new IOException("Injected observation outage"); }));
        if (boundary == "save")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => H.Publish(s, service, caller.Token));
        else Assert.Equal(boundary == "after-commit" && !unavailable ? JobPublicationState.AlreadyPublished : JobPublicationState.Indeterminate,
            (await H.Publish(s, service, caller.Token)).State);
        Assert.True(reached);
        if (boundary != "after-commit") Assert.Equal(before, await H.Snapshot());
        else Assert.Equal(JobPublicationState.AlreadyPublished, (await H.Publish(s)).State);
        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.RequirementProcessingJobs.SingleAsync(Token)).CancellationRequestedAtUtc);
    }
}
