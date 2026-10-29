using System.Data.Common;
using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    private sealed class CommitGate : DbTransactionInterceptor
    {
        internal TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = Signal();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await Release.Task.WaitAsync(cancellationToken); return result;
        }
    }

    private async Task<Seed> OtherJob(Seed first)
    {
        var now = await H.Now(); await using var db = fixture.CreateDbContext();
        var oldQuote = await db.PreQuotes.SingleAsync(x => x.Id == first.Quote, Token);
        var quote = PreQuote.Create(oldQuote.ProjectId, first.Actor, "PC-2026-0002", null, now);
        var requirement = Requirement.Create(quote.Id, first.Actor, RequirementCommercialLine.Essential, now);
        var job = RequirementProcessingJob.CreateForVerifiedPublication(requirement.Id, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, now);
        db.AddRange(quote, requirement, job); await db.SaveChangesAsync(Token);
        return new(new(job.Id, job.ExecutionRequestId, requirement.Id, "", 1), first.Actor, quote.Id);
    }

    [Theory]
    [InlineData("different")] [InlineData("same")] [InlineData("collision")]
    [InlineData("cancel-first")] [InlineData("claim-first")]
    public async Task Races_ObservedPostgreSqlBlocking_OneAssignment_NoAttempts(string scenario)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        var other = scenario == "collision" ? await OtherJob(s) : s;
        var next = scenario == "same" ? e : Request(other);
        if (scenario == "collision") next = next with { OperationId = e.OperationId };
        var protectedBefore = await Snapshot(false);
        var gate = new CommitGate();
        var waiting = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Commands { Before = (cmd, _) =>
        {
            if (scenario == "collision" ? cmd.CommandText.StartsWith("WITH clock") : cmd.CommandText.Contains("pre_quotes") && cmd.CommandText.Contains("FOR UPDATE"))
                waiting.TrySetResult(((NpgsqlConnection)cmd.Connection!).ProcessID);
            return Task.CompletedTask;
        }};
        Task<LeaseOperationResult>? claim1 = null, claim2 = null;
        Task<JobTerminationResult>? cancel = null;
        try
        {
            if (scenario == "cancel-first") cancel = H.Call("request", s, H.Service(gate));
            else claim1 = Service(gate).ClaimInitialV2Async(e, s.Actor, Token);
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            if (scenario == "claim-first") cancel = H.Call("request", s, H.Service(probe));
            else claim2 = Service(probe).ClaimInitialV2Async(scenario == "cancel-first" ? e : next, s.Actor, Token);
            var loser = await waiting.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Claim race {scenario}: winner PID {winner}, blocked PID {loser}; pg_blocking_pids observed.");
            gate.Release.TrySetResult();
            if (claim1 is not null) Assert.Equal(LeaseOperationState.Claimed, (await claim1).State);
            if (claim2 is not null)
            {
                var result = await claim2;
                Assert.Equal(scenario == "same" ? LeaseOperationState.AlreadyApplied
                    : scenario == "cancel-first" ? LeaseOperationState.Rejected : LeaseOperationState.Conflict, result.State);
                if (scenario == "same") Assert.Equal(JsonSerializer.Serialize((await claim1!).Receipt), JsonSerializer.Serialize(result.Receipt));
            }
            if (cancel is not null) Assert.Equal(scenario == "cancel-first" ? JobTerminationState.CancellationConfirmed
                : JobTerminationState.CancellationRequested, (await cancel).State);
            await using var db = fixture.CreateDbContext();
            Assert.Equal(scenario == "cancel-first" ? 0 : 1, await db.JobLeaseOperations.CountAsync(Token));
            Assert.Empty(await db.RequirementProcessingAttempts.ToListAsync(Token));
            var job = await Job(e.JobId);
            Assert.Equal(scenario == "cancel-first" ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Running, job.State);
            if (scenario == "collision") Assert.Equal(RequirementProcessingJobState.Queued, (await Job(next.JobId)).State);
            if (scenario == "claim-first") Assert.NotNull(job.CancellationRequestedAtUtc);
            Assert.Equal(protectedBefore, await Snapshot(false));
        }
        finally
        {
            gate.Release.TrySetResult();
            if (claim1 is not null) await claim1;
            if (claim2 is not null) await claim2;
            if (cancel is not null) await cancel;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureBeforeCommit_RollsBackJobAndReceipt(bool cancelCaller)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var before = await Snapshot();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var injected = false;
        var probe = new Commands { After = async (cmd, reader, _) =>
        {
            if (!cmd.CommandText.StartsWith("WITH clock")) return;
            injected = true;
            await reader.DisposeAsync();
            if (cancelCaller) caller.Cancel();
            throw new IOException("injected after atomic statement, before commit");
        }};
        if (cancelCaller)
        {
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Service(probe).ClaimInitialV2Async(Request(s), s.Actor, caller.Token));
            Assert.Equal(caller.Token, error.CancellationToken);
        }
        else Assert.Equal(LeaseOperationState.PersistenceError, (await Service(probe).ClaimInitialV2Async(Request(s), s.Actor, caller.Token)).State);
        Assert.True(injected); Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task CommitResponseLoss_IndependentObservation_OriginalCancellation(bool cancelCaller, bool failObservation)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var committed = false; var observing = Signal(); var release = Signal();
        Guid? writeContext = null, readContext = null; CancellationToken observedToken = default;
        var identities = new Contexts(id => { if (committed) readContext = id; else writeContext = id; });
        var commands = new Commands { Before = async (cmd, ct) =>
        {
            if (!committed || !cmd.CommandText.Contains("pre_quotes") || !cmd.CommandText.Contains("FOR UPDATE")) return;
            observedToken = ct; observing.TrySetResult(); await release.Task.WaitAsync(ct);
            if (failObservation) throw new IOException("observer unavailable");
        }};
        var hook = new JobProtocolTestSupport.AfterCommit(() => { committed = true; throw new IOException("response lost after real commit"); });
        var task = Service(identities, commands, hook).ClaimInitialV2Async(e, s.Actor, caller.Token);
        try
        {
            await observing.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.False(task.IsCompleted); Assert.NotEqual(caller.Token, observedToken);
            if (cancelCaller) caller.Cancel();
            Assert.False(observedToken.IsCancellationRequested);
            release.TrySetResult();
            if (cancelCaller)
            {
                var error = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
                Assert.Equal(caller.Token, error.CancellationToken);
            }
            else Assert.Equal(failObservation ? LeaseOperationState.Indeterminate : LeaseOperationState.AlreadyApplied, (await task).State);
            Assert.True(committed); Assert.NotNull(writeContext); Assert.NotNull(readContext); Assert.NotEqual(writeContext, readContext);
            Assert.Equal(1, commands.Writes);
            var job = await Job(e.JobId); Assert.Equal(RequirementProcessingJobState.Running, job.State);
            Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.ProcessingAttemptId);
            var before = await Snapshot(); var replayProbe = new Commands();
            var replay = await Service(replayProbe).ClaimInitialV2Async(e, s.Actor, Token);
            Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State); Assert.Equal(0, replayProbe.Writes);
            Assert.Equal(before, await Snapshot());
        }
        finally { release.TrySetResult(); try { await task; } catch (OperationCanceledException) { } }
    }

    private sealed class Contexts(Action<Guid> record) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { record(eventData.Context!.ContextId.InstanceId); return ValueTask.FromResult(result); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WaitingForExactJob_ClockAfterLocks_OrOriginalCancellation(bool cancelCaller)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var holder = fixture.CreateDbContext();
        await using var tx = await holder.Database.BeginTransactionAsync(Token);
        await holder.PreQuotes.FromSqlInterpolated($"SELECT * FROM core.pre_quotes WHERE id={s.Quote} FOR UPDATE").ToListAsync(Token);
        var probe = new JobProtocolTestSupport.QuoteProbe();
        var task = Service(probe).ClaimInitialV2Async(e, s.Actor, caller.Token);
        try
        {
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var blocker = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
            await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocker);
            var afterWait = await H.Now();
            output.WriteLine($"Claim wait/cancel={cancelCaller}: PID {pid} blocked by {blocker}; clock floor {afterWait:O}.");
            if (cancelCaller) caller.Cancel();
            await tx.RollbackAsync(Token);
            if (cancelCaller)
            {
                var error = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
                Assert.Equal(caller.Token, error.CancellationToken);
                var job = await Job(e.JobId); Assert.Equal(RequirementProcessingJobState.Queued, job.State);
                Assert.Null(job.CancellationRequestedAtUtc);
                await using var read = fixture.CreateDbContext(); Assert.Empty(await read.JobLeaseOperations.ToListAsync(Token));
            }
            else
            {
                var result = await task; Assert.Equal(LeaseOperationState.Claimed, result.State);
                Assert.True(result.Receipt!.AppliedAtUtc >= afterWait);
            }
        }
        finally { await tx.DisposeAsync(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData("absent")] [InlineData("changed-dates")] [InlineData("denied")]
    public async Task Uncertainty_RequiresExactOwnCandidate_NotJustRunning(string scenario)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        var injected = false;
        var hook = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            await using var db = fixture.CreateDbContext();
            if (scenario == "absent") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE operation_id={e.OperationId}", Token);
            if (scenario == "changed-dates")
            {
                await using var tx = await db.Database.BeginTransactionAsync(Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET assigned_at_utc=assigned_at_utc+interval '1 microsecond',applied_at_utc=applied_at_utc+interval '1 microsecond',expires_at_utc=expires_at_utc+interval '1 microsecond' WHERE operation_id={e.OperationId}", Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET started_at_utc=started_at_utc+interval '1 microsecond',lease_expires_at_utc=lease_expires_at_utc+interval '1 microsecond' WHERE id={e.JobId}", Token);
                await tx.CommitAsync(Token);
            }
            if (scenario == "denied") { (await db.Users.SingleAsync(Token)).Deactivate(await H.Now()); await db.SaveChangesAsync(Token); }
            injected = true; throw new IOException("response lost; observation no longer demonstrates own candidate");
        });
        var probe = new Commands();
        var result = await Service(hook, probe).ClaimInitialV2Async(e, s.Actor, Token);
        Assert.True(injected); Assert.Equal(LeaseOperationState.Indeterminate, result.State);
        Assert.Null(result.Receipt); Assert.Null(result.Observation); Assert.Equal(1, probe.Writes);
        Assert.Equal(RequirementProcessingJobState.Running, (await Job(e.JobId)).State);
    }

    [Fact]
    public async Task ObservationBlocked_UsesIndependentTenSecondBudget_NoCompensation()
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        await using var blocker = fixture.CreateDbContext(); await blocker.Database.OpenConnectionAsync(Token);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? held = null;
        var committed = false; var probe = new JobProtocolTestSupport.QuoteProbe(() => committed);
        var hook = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            held = await blocker.Database.BeginTransactionAsync(Token);
            await blocker.PreQuotes.FromSqlInterpolated($"SELECT * FROM core.pre_quotes WHERE id={s.Quote} FOR UPDATE").ToListAsync(Token);
            committed = true; throw new IOException("response loss");
        });
        var task = Service(hook, probe).ClaimInitialV2Async(e, s.Actor, Token);
        try
        {
            var observer = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var blocking = ((NpgsqlConnection)blocker.Database.GetDbConnection()).ProcessID;
            await new JobProtocolTestSupport(fixture).WaitBlocked(observer, blocking);
            output.WriteLine($"Claim observation PID {observer} blocked by {blocking}; independent timeout observed.");
            await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(LeaseOperationState.Indeterminate, (await task).State);
        }
        finally { if (held is not null) await held.DisposeAsync(); await task; }
        var job = await Job(e.JobId); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.ProcessingAttemptId);
        await using var read = fixture.CreateDbContext(); Assert.Single(await read.JobLeaseOperations.ToListAsync(Token));
    }

    [Fact]
    public async Task CallerCancelledAfterSuccessfulCommit_NoLateSuccess_NoBusinessCancellation()
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new JobProtocolTestSupport.AfterCommit(() => { caller.Cancel(); return Task.CompletedTask; });
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Service(hook).ClaimInitialV2Async(e, s.Actor, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        var job = await Job(e.JobId); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.ProcessingAttemptId);
        Assert.Equal(LeaseOperationState.AlreadyApplied, (await Service().ClaimInitialV2Async(e, s.Actor, Token)).State);
    }
}
