using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobTerminationConcurrencyPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private JobTerminationTestSupport H => new(fixture);

    [Theory]
    [InlineData("request", "request")]
    [InlineData("confirm", "confirm")]
    [InlineData("request", "fail")]
    [InlineData("fail", "request")]
    public async Task OrderedRaces_IndependentConnections_OneEffect(string firstOp, string secondOp)
    {
        var s = await H.Create(attempt: true, request: firstOp == "confirm");
        var saved = Signal(); var release = Signal(); var competing = Signal();
        var pid1 = 0; var pid2 = 0;
        var first = H.Call(firstOp, s, H.Service(new CommandHook(async command =>
        {
            pid1 = ((NpgsqlConnection)command.Connection!).ProcessID;
            saved.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        })));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        var second = H.Call(secondOp, s, H.Service(new CommandHook(beforeQuoteLock: command =>
        {
            pid2 = ((NpgsqlConnection)command.Connection!).ProcessID;
            competing.TrySetResult();
        })));
        await competing.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        release.SetResult();
        var result = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.NotEqual(pid1, pid2);
        Assert.Equal(firstOp switch { "request" => JobTerminationState.CancellationRequested,
            "confirm" => JobTerminationState.CancellationConfirmed, _ => JobTerminationState.Failed }, result[0].State);
        if (firstOp == secondOp)
        {
            Assert.Equal(JobTerminationState.AlreadyRecognized, result[1].State);
            Assert.Equal(result[0].Receipt, result[1].Receipt);
        }
        else
        {
            Assert.Equal(JobTerminationState.Conflict, result[1].State);
            Assert.Equal(firstOp == "request" ? JobTerminationFailure.CancellationRequested : JobTerminationFailure.TerminalConflict, result[1].Failure);
        }
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        Assert.Equal(result[0].Receipt!.CancellationRequestedAtUtc, job.CancellationRequestedAtUtc);
        Assert.Equal(result[0].Receipt!.FinishedAtUtc, job.FinishedAtUtc);
        Assert.Equal(1, await db.RequirementProcessingAttempts.CountAsync(Token));
        Assert.Equal(1, job.AttemptCount);
    }

    [Theory]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("fail", false)]
    [InlineData("fail", true)]
    public async Task RelatedWrites_RollBackOnInjectedFailureOrExpiryAtGuard(string operation, bool expire)
    {
        var s = await H.Create(attempt: true, request: operation == "confirm");
        var expiry = (await H.Now()).AddSeconds(4);
        if (expire)
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {expiry} WHERE id = {s.Execution.JobId}", Token);
        }
        var before = await H.Snapshot();
        var saved = Signal(); var release = Signal();
        var pending = H.Call(operation, s, H.Service(new Saved(async _ =>
        {
            if (!expire) throw new IOException("test failure after related writes");
            Assert.True(await H.Now() < expiry);
            saved.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        })));
        if (expire)
        {
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (await H.Now() < expiry) await Task.Delay(25, deadline.Token);
            release.SetResult();
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.Equal(expire ? JobTerminationFailure.LeaseConflict : JobTerminationFailure.PersistenceError, result.Failure);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData("request", false)]
    [InlineData("request", true)]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("fail", false)]
    [InlineData("fail", true)]
    public async Task LostCommitResponse_ExactEffectRecognized_OrIndeterminateWithoutCompensation(string operation, bool unavailable)
    {
        var s = await H.Create(attempt: true, request: operation == "confirm");
        var committed = false;
        var result = await H.Call(operation, s, H.Service(
            new CommitHook(() => { committed = true; throw new IOException("simulated lost response"); }),
            new OpenHook(() => { if (committed && unavailable) throw new IOException("simulated observation outage"); })));
        Assert.True(committed);
        Assert.Equal(unavailable ? JobTerminationState.Indeterminate : JobTerminationState.AlreadyRecognized, result.State);
        var snapshot = await H.Snapshot();
        var replay = await H.Call(operation, s);
        Assert.Equal(JobTerminationState.AlreadyRecognized, replay.State);
        Assert.Equal(operation switch { "request" => "Running", "confirm" => "Cancelled", _ => "Failed" }, replay.Receipt!.JobState);
        Assert.Equal(s.Execution.ProcessingAttemptId, replay.Receipt.ProcessingAttemptId);
        Assert.Equal(snapshot, await H.Snapshot());
    }

    [Theory]
    [InlineData("start")]
    [InlineData("request")]
    [InlineData("confirm")]
    [InlineData("fail")]
    public async Task TokenCancelledBeforeCommit_RollsBack_NoBusinessCancellation(string operation)
    {
        var s = await H.Create(attempt: operation != "start", request: operation == "confirm");
        var before = await H.Snapshot();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var saved = new Saved(_ => { caller.Cancel(); return Task.CompletedTask; });
        if (operation == "start")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => H.StartService(saved).StartAsync(s.Execution, s.Actor, caller.Token));
        else
        {
            var service = operation == "request"
                ? H.Service(new CommandHook(_ => { caller.Cancel(); throw new OperationCanceledException(caller.Token); }))
                : H.Service(saved);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => H.Call(operation, s, service, caller.Token));
        }
        Assert.Equal(before, await H.Snapshot());
    }

    public static IEnumerable<object[]> CommitTokenCases()
    {
        foreach (var op in new[] { "start", "request", "confirm", "fail" })
        {
            yield return [op, false, false];
            yield return [op, true, false];
            yield return [op, true, true];
        }
    }

    [Theory]
    [MemberData(nameof(CommitTokenCases))]
    public async Task TokenCancelledAtCommit_UsesIndependentBoundedObservation(string operation, bool afterCommit, bool unavailable)
    {
        var s = await H.Create(attempt: operation != "start", request: operation == "confirm");
        var before = await H.Snapshot();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var boundaryReached = false;
        var commit = new CommitHook(() =>
        {
            boundaryReached = true;
            caller.Cancel();
            throw new OperationCanceledException(caller.Token);
        }, after: afterCommit);
        var open = new OpenHook(() =>
        {
            if (boundaryReached && unavailable) throw new IOException("simulated observation outage");
        });
        if (operation == "start")
        {
            var result = await H.StartService(commit, open).StartAsync(s.Execution, s.Actor, caller.Token);
            Assert.Equal(afterCommit && !unavailable ? AssignedJobStartState.AlreadyStarted : AssignedJobStartState.Indeterminate, result.State);
        }
        else
        {
            var result = await H.Call(operation, s, H.Service(commit, open), caller.Token);
            Assert.Equal(afterCommit && !unavailable ? JobTerminationState.AlreadyRecognized : JobTerminationState.Indeterminate, result.State);
        }
        Assert.True(boundaryReached);
        if (!afterCommit) Assert.Equal(before, await H.Snapshot());
        else
        {
            var durable = await H.Snapshot();
            Assert.NotEqual(before, durable);
            if (operation == "start")
                Assert.Equal(AssignedJobStartState.AlreadyStarted, (await H.StartService().StartAsync(s.Execution, s.Actor, Token)).State);
            else Assert.Equal(JobTerminationState.AlreadyRecognized, (await H.Call(operation, s)).State);
            Assert.Equal(durable, await H.Snapshot());
        }
    }
}
