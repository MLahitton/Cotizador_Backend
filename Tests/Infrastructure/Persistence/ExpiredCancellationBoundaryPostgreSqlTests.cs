using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData("error")] [InlineData("guard")] [InlineData("caller")]
    public async Task ExpiredB_PostSave_RollbackAll_NoReceipt(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var saved = new Saved(async db =>
        {
            Assert.DoesNotContain(db.ChangeTracker.Entries(), x => x.Entity is RequirementProcessingJob);
            if (mode == "guard") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={s.Input.ExpectedLeaseExpiresAtUtc.AddMinutes(10)}", Token);
            else { if (mode == "caller") caller.Cancel(); throw new IOException("post-save failure with no open reader"); }
        });
        var task = ExpiredCloser(writes, saved).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, caller.Token);
        if (mode == "caller") Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
        else ExpiredRejected(await task, mode == "guard" ? JobTerminationFailure.LeaseConflict : JobTerminationFailure.PersistenceError);
        Assert.True(writes.Writes > 0); Assert.Equal(before, await ExpiredSnapshot());
    }

    [Theory]
    [InlineData("recognized")] [InlineData("unavailable")] [InlineData("absent")]
    [InlineData("candidate")] [InlineData("denied")] [InlineData("caller-commit")]
    [InlineData("caller-observer")] [InlineData("caller-unavailable")]
    public async Task ExpiredB_CommitLoss_ExactOwnCandidate_OriginalCancellation(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var committed = false; var calls = 0; CancellationToken independent = default, action = default;
        var writes = new CombinedCommands();
        var after = new SupervisionAfterCommit(async () =>
        {
            committed = true;
            await using var db = fixture.CreateDbContext();
            if (mode == "absent") await db.Database.ExecuteSqlRawAsync("DELETE FROM core.expired_execution_cancellations", Token);
            if (mode == "candidate") await db.Database.ExecuteSqlRawAsync("UPDATE core.expired_execution_cancellations SET guard_at_utc=guard_at_utc+interval '1 microsecond'", Token);
            if (mode == "denied") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET is_active=false WHERE id={s.Seed.Actor}", Token);
            if (mode == "caller-commit") caller.Cancel();
            throw new IOException("response lost after real commit");
        });
        var open = new ClosureOpenToken(ct =>
        {
            calls++;
            if (!committed) { action = ct; return; }
            independent = ct;
            if (mode is "caller-observer" or "caller-unavailable") caller.Cancel();
            if (mode is "unavailable" or "caller-unavailable") throw new IOException("observer unavailable");
        });
        var task = ExpiredCloser(writes, open, after).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, caller.Token);
        if (mode.StartsWith("caller")) Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
        else
        {
            var r = await task;
            Assert.Equal(mode == "recognized" ? JobTerminationState.AlreadyRecognized : JobTerminationState.Indeterminate, r.Result.State);
            if (mode == "recognized") Assert.Equal(s.Input.OperationId, r.Receipt!.OperationId);
            else { Assert.Null(r.Receipt); Assert.Null(r.Result.Receipt); }
        }
        Assert.True(committed); Assert.Equal(2, calls); Assert.NotEqual(action, independent); Assert.False(independent.IsCancellationRequested);
        Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
        await using var read = fixture.CreateDbContext(); Assert.Equal(mode == "absent" ? 0 : 1, await read.ExpiredExecutionCancellations.CountAsync(Token));
        // One EF SaveChanges batch plus one guarded CTE, never an observer write.
        Assert.Equal(2, writes.Writes);
        output.WriteLine($"B commit-loss {mode}: real Cancelled preserved, observer independent, no retry.");
    }

    [Theory]
    [InlineData("budget")] [InlineData("caller")] [InlineData("observer")]
    public async Task ExpiredB_BlockedContexts_BoundedBudgetAndIndependentObserver(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        var before = await ExpiredSnapshot();
        await using var held = fixture.CreateDbContext(); await using var tx = await held.Database.BeginTransactionAsync(Token);
        async Task Hold() => await held.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE", Token);
        if (mode != "observer") await Hold();
        var seen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seenToken = default;
        var probe = new ExpiredCommand(async (cmd, ct) =>
        {
            if (cmd.CommandText.Contains("core.pre_quotes") && cmd.CommandText.Contains(mode == "observer" ? "FOR SHARE" : "FOR UPDATE"))
            { seenToken = ct; seen.TrySetResult(((NpgsqlConnection)cmd.Connection!).ProcessID); }
            await Task.CompletedTask;
        });
        var after = new SupervisionAfterCommit(async () => { await Hold(); throw new IOException("lost after commit"); });
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var task = ExpiredCloser(mode == "observer" ? [probe, after] : [probe]).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, caller.Token);
        try
        {
            var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); var blocker = ((NpgsqlConnection)held.Database.GetDbConnection()).ProcessID;
            Assert.NotEqual(pid, blocker); await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocker);
            if (mode == "caller") caller.Cancel();
            if (mode == "caller") Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
            else
            {
                var r = await task; Assert.Equal(mode == "observer" ? JobTerminationState.Indeterminate : JobTerminationState.Conflict, r.Result.State);
                Assert.Equal(mode == "observer" ? JobTerminationFailure.InconsistentEvidence : JobTerminationFailure.PersistenceError, r.Result.Failure);
                Assert.Null(r.Receipt); Assert.True(seenToken.IsCancellationRequested);
            }
            output.WriteLine($"B {mode}: blocked PID={pid}, blocker={blocker}, pg_blocking_pids observed.");
        }
        finally { await tx.RollbackAsync(Token); try { await task; } catch (OperationCanceledException) { } }
        if (mode != "observer") Assert.Equal(before, await ExpiredSnapshot());
        else Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
    }

    private sealed class ExpiredCommand(Func<DbCommand, CancellationToken, Task> before) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { await before(command, cancellationToken); return result; }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExpiredB_UnknownRollbackOrIdentityGuard_NeverFalseConfirmation(bool unknownRollback)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files); var before = await ExpiredSnapshot();
        var saved = new Saved(async db =>
        {
            if (unknownRollback)
            {
                await db.Database.GetDbConnection().CloseAsync();
                throw new IOException("connection closed before rollback acknowledgement");
            }
            await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET cancellation_requested_at_utc=cancellation_requested_at_utc+interval '1 microsecond'", Token);
        });
        var result = await ExpiredCloser(saved).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
        if (unknownRollback)
        {
            Assert.Equal(JobTerminationState.Indeterminate, result.Result.State); Assert.Null(result.Receipt); Assert.Null(result.Result.Receipt);
        }
        else ExpiredRejected(result, JobTerminationFailure.IdentityConflict);
        Assert.Equal(before, await ExpiredSnapshot());
    }

    [Fact]
    public async Task ExpiredB_FinalTimeGuardLoses_RollsBackSavedEntities()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files); var before = await ExpiredSnapshot();
        var changed = false;
        var guard = new ExpiredCommand((cmd, _) =>
        {
            if (cmd.CommandText.StartsWith("WITH clock"))
            {
                // Fault injection of a losing final time comparison, not a simulated PostgreSQL clock measurement.
                Assert.Contains("AND clock.g>=", cmd.CommandText);
                cmd.CommandText = cmd.CommandText.Replace("AND clock.g>=", "AND clock.g - interval '1 day'>=", StringComparison.Ordinal);
                changed = true;
            }
            return Task.CompletedTask;
        });
        ExpiredRejected(await ExpiredCloser(guard).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token), JobTerminationFailure.LeaseConflict);
        Assert.True(changed); Assert.Equal(before, await ExpiredSnapshot());
    }

    [Fact]
    public async Task ExpiredB_OneActionBudget_LateConfirmedCommitPreserved()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        CancellationToken first = default; var commands = 0;
        var probe = new ExpiredCommand((_, ct) => { if (commands++ == 0) first = ct; else Assert.Equal(first, ct); return Task.CompletedTask; });
        var after = new SupervisionAfterCommit(async () =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, first); } catch (OperationCanceledException) when (first.IsCancellationRequested) { }
        });
        var result = await ExpiredCloser(probe, after).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
        Assert.True(first.IsCancellationRequested); Assert.False(Token.IsCancellationRequested);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.Result.State); Assert.NotNull(result.Receipt);
    }
}
