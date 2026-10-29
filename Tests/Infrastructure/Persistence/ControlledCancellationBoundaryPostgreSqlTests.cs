using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class ClosureBoundary(IExecutionClosureObservationStore inner, Func<Task> after) : IExecutionClosureObservationStore
    {
        public async Task<ClosureObservationResult> ObserveForClosureAsync(ExecutionClosureIdentity e, Guid actor, CancellationToken ct)
        { var result = await inner.ObserveForClosureAsync(e, actor, ct); await after(); return result; }
    }

    [Theory]
    [InlineData("owner")] [InlineData("concurrent-close")] [InlineData("final-guard")]
    public async Task ClosureA_Revalidation_AfterRead_NoExternalLocks(string mode)
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        async Task After()
        {
            if (mode == "concurrent-close") Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Call("confirm", s.Seed)).State);
            if (mode == "owner")
            {
                await using var db = fixture.CreateDbContext();
                var user = User.CreateFromGoogle("closure-race@test.local", "Next", null, null, await H.Now()); db.Add(user); await db.SaveChangesAsync(Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={user.Id}", Token);
            }
        }
        var saved = new Saved(async db =>
        {
            var expired = s.Drain.AssignedAtUtc.AddTicks(10);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expired} WHERE id={s.Input.Identity.JobId}", Token);
        });
        var service = new ConfirmStoppedCancellationService(new(new ClosureBoundary(new ExecutionClosureObservationStore(H.Options()), After)),
            H.Service(mode == "final-guard" ? [writes, saved] : [writes]));
        var result = await service.ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, Token);
        if (mode == "concurrent-close")
        {
            Assert.Equal(JobTerminationState.AlreadyRecognized, result.State); Assert.Equal(0, writes.Writes);
            Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
        }
        else
        {
            Assert.Equal(JobTerminationState.Conflict, result.State);
            Assert.Equal(mode == "owner" ? JobTerminationFailure.NotFound : JobTerminationFailure.LeaseConflict, result.Failure);
            Assert.Null(result.Receipt); Assert.Equal(before, await CombinedSnapshot());
            if (mode == "owner") Assert.Equal(0, writes.Writes); else Assert.True(writes.Writes > 0);
        }
        output.WriteLine($"Closure A {mode}: actual Confirm revalidation; final-guard mutations roll back including attempt/Requirement/activity.");
    }

    private sealed class ClosureShareProbe : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Seen;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("core.pre_quotes") && command.CommandText.Contains("FOR SHARE"))
            { Seen = cancellationToken; Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID); }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("budget")] [InlineData("caller")] [InlineData("expiry")]
    public async Task ClosureA_Waits_PostgreSqlBudgetCallerAndExpiry(string mode)
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        DateTimeOffset? horizon = null;
        if (mode == "expiry")
        {
            horizon = (await H.Now()).AddSeconds(2);
            await using var prep = fixture.CreateDbContext();
            await prep.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={horizon} WHERE id={s.Input.Identity.JobId}", Token);
        }
        var before = await CombinedSnapshot();
        await using var held = fixture.CreateDbContext(); await using var tx = await held.Database.BeginTransactionAsync(Token);
        async Task Lock() => await held.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE", Token);
        if (mode != "expiry") await Lock();
        var share = new ClosureShareProbe(); var confirm = new JobProtocolTestSupport.QuoteProbe();
        var readStore = new ExecutionClosureObservationStore(H.Options(share));
        IExecutionClosureObservationStore reader = mode == "expiry" ? new ClosureBoundary(readStore, Lock) : readStore;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var service = new ConfirmStoppedCancellationService(new(reader), H.Service(confirm));
        var running = service.ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, caller.Token);
        var released = false;
        try
        {
            var blocked = await (mode == "expiry" ? confirm.Reached.Task : share.Reached.Task).WaitAsync(TimeSpan.FromSeconds(20), Token);
            var blocker = ((NpgsqlConnection)held.Database.GetDbConnection()).ProcessID;
            Assert.NotEqual(blocker, blocked); await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, blocker);
            if (mode == "caller") caller.Cancel();
            if (mode == "expiry")
            {
                Assert.True(horizon > s.Drain.AssignedAtUtc);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                while (await H.Now() <= horizon) await Task.Delay(20, deadline.Token);
                output.WriteLine($"Closure A expiry observed DB={await H.Now():O} > {horizon:O}, before releasing {blocker}/{blocked}.");
                await tx.RollbackAsync(Token); released = true;
                var result = await running; Assert.Equal(JobTerminationState.Conflict, result.State); Assert.Equal(JobTerminationFailure.LeaseConflict, result.Failure);
            }
            else if (mode == "caller") Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => running)).CancellationToken);
            else
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(20), Token);
                Assert.Equal(JobTerminationState.Indeterminate, result.State); Assert.Null(result.Receipt);
                Assert.True(share.Seen.IsCancellationRequested); Assert.False(caller.IsCancellationRequested);
            }
            output.WriteLine($"Closure A {mode}: PID {blocked} blocked by {blocker}, pg_blocking_pids observed.");
        }
        finally { if (!released) await tx.RollbackAsync(Token); try { await running; } catch (OperationCanceledException) { } }
        Assert.Equal(before, await CombinedSnapshot());
    }

    [Theory]
    [InlineData("recognized")] [InlineData("unavailable")] [InlineData("caller-commit")]
    [InlineData("caller-observer")] [InlineData("caller-before")]
    public async Task ClosureA_CommitLoss_OriginalCaller_ConfirmedStatePreserved(string mode)
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var committed = false; var opened = 0; var writes = new CombinedCommands(); CancellationToken observerToken = default;
        var after = new CommitHook(() =>
        {
            committed = true;
            if (mode == "caller-commit") caller.Cancel();
            throw new IOException("actual Confirm commit response lost");
        });
        var opening = new ClosureOpenToken((ct) =>
        {
            opened++;
            if (!committed) return;
            observerToken = ct;
            if (mode == "caller-observer") caller.Cancel();
            if (mode == "unavailable") throw new IOException("independent observer unavailable");
        });
        var service = Closer(write: [writes, after, opening]);
        if (mode == "caller-before") caller.Cancel();
        var task = service.ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, caller.Token);
        if (mode.StartsWith("caller")) Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
        else
        {
            var result = await task;
            Assert.Equal(mode == "recognized" ? JobTerminationState.AlreadyRecognized : JobTerminationState.Indeterminate, result.State);
            if (mode == "unavailable") Assert.Null(result.Receipt); else Assert.NotNull(result.Receipt);
        }
        Assert.Equal(mode != "caller-before", committed);
        var job = await CombinedJob();
        Assert.Equal(committed ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Running, job.State);
        if (committed)
        {
            Assert.True(opened >= 2); Assert.NotEqual(caller.Token, observerToken); Assert.False(observerToken.IsCancellationRequested);
            var all = await CombinedSnapshot(); var replayWrites = new CombinedCommands();
            Assert.Equal(JobTerminationState.AlreadyRecognized, (await Closer(write: [replayWrites])
                .ConfirmStoppedCancellationAsync(s.Input, null, s.Seed.Actor, Token)).State);
            Assert.Equal(0, replayWrites.Writes); Assert.Equal(all, await CombinedSnapshot());
        }
        else { Assert.Equal(0, opened); Assert.Equal(0, writes.Writes); }
    }

    private sealed class ClosureOpenToken(Action<CancellationToken> action) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { action(cancellationToken); return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task ClosureA_OneBudgetAcrossReadAndConfirm_LateConfirmedCommitNotErased()
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        CancellationToken readToken = default, writeToken = default;
        var read = new ClosureOpenToken(ct => readToken = ct);
        var write = new ClosureOpenToken(ct => writeToken = ct);
        var committed = false;
        var late = new SupervisionAfterCommit(async () =>
        {
            committed = true;
            Assert.Equal(readToken, writeToken);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, writeToken); }
            catch (OperationCanceledException) when (writeToken.IsCancellationRequested) { }
        });
        var result = await Closer([read], [write, late]).ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, Token);
        Assert.True(committed); Assert.True(writeToken.IsCancellationRequested); Assert.False(Token.IsCancellationRequested);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.State); Assert.NotNull(result.Receipt);
        Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
        output.WriteLine("Closure A: exact same action token at read and mutation, one budget expired after real commit; confirmed result preserved.");
    }
}
