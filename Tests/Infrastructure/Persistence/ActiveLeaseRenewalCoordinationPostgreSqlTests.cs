using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    [Theory]
    [InlineData("same")] [InlineData("different")] [InlineData("no-extension")] [InlineData("collision")]
    [InlineData("request-first")] [InlineData("renew-request")]
    [InlineData("start-first")] [InlineData("renew-start")]
    [InlineData("terminal-first")] [InlineData("renew-terminal")]
    public async Task Renew_Races_ObservedLocks_ExactEffects(string scenario)
    {
        var s = await RenewalSetup(attempt: scenario.Contains("terminal"), noExtension: scenario == "no-extension");
        var other = scenario == "collision" ? await ClaimForRenewal(await OtherJob(s.Seed)) : s;
        var second = scenario == "same" ? s.Request : other.Request with { OperationId = Guid.NewGuid() };
        if (scenario == "collision") second = second with { OperationId = s.Request.OperationId };
        var before = await Snapshot(false); var otherBefore = scenario == "collision" ? System.Text.Json.JsonSerializer.Serialize(await Job(second.JobId)) : null;
        var gate = new CommitGate(); var waiting = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Commands { Before = (cmd, _) =>
        {
            if (scenario == "collision" ? cmd.CommandText.StartsWith("WITH clock") : cmd.CommandText.Contains("pre_quotes") && cmd.CommandText.Contains("FOR UPDATE"))
                waiting.TrySetResult(((NpgsqlConnection)cmd.Connection!).ProcessID);
            return Task.CompletedTask;
        }};
        Task<LeaseOperationResult>? firstRenew = null, secondRenew = null;
        Task<JobTerminationResult>? terminate = null;
        Task<AssignedJobStartResult>? start = null;
        try
        {
            if (scenario == "request-first") terminate = H.Call("request", s.Seed, H.Service(gate));
            else if (scenario == "terminal-first") terminate = H.Call("fail", s.Seed, H.Service(gate));
            else if (scenario == "start-first") start = new AssignedJobStartStore(H.Options(gate)).StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
            else firstRenew = RenewService(gate).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            if (scenario == "renew-request") terminate = H.Call("request", s.Seed, H.Service(probe));
            else if (scenario == "renew-terminal") terminate = H.Call("fail", s.Seed, H.Service(probe));
            else if (scenario == "renew-start") start = new AssignedJobStartStore(H.Options(probe)).StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
            else secondRenew = RenewService(probe).RenewActiveV2Async(second, other.Seed.Actor, Token);
            var loser = await waiting.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Renew race {scenario}: winner PID {winner}, blocked PID {loser}; pg_blocking_pids observed.");
            gate.Release.TrySetResult();
            if (firstRenew is not null)
            {
                var result = await firstRenew; Assert.Equal(LeaseOperationState.Renewed, result.State);
                Assert.Equal(scenario != "no-extension", result.Receipt!.IsEffectiveExtension);
            }
            if (secondRenew is not null)
            {
                var result = await secondRenew;
                if (scenario == "same") Assert.Equal(LeaseOperationState.AlreadyApplied, result.State);
                else if (scenario == "no-extension")
                {
                    Assert.Equal(LeaseOperationState.Renewed, result.State); Assert.False(result.Receipt!.IsEffectiveExtension);
                    Assert.Equal(s.Request.ExpectedLeaseExpiresAtUtc, result.Receipt.ExpiresAtUtc);
                    Assert.NotEqual((await firstRenew!).Receipt!.OperationId, result.Receipt.OperationId);
                }
                else
                {
                    var expected = scenario switch { "different" => LeaseOperationFailure.StaleLease,
                        "collision" => LeaseOperationFailure.ReceiptConflict, "request-first" => LeaseOperationFailure.CancellationRequested,
                        "start-first" => LeaseOperationFailure.AttemptConflict, _ => LeaseOperationFailure.Terminal };
                    Assert.Equal(scenario is "request-first" or "terminal-first" ? LeaseOperationState.Rejected : LeaseOperationState.Conflict, result.State);
                    Assert.Equal(expected, result.Failure); Assert.Null(result.Receipt); Assert.Null(result.Observation);
                }
            }
            if (start is not null) Assert.Equal(AssignedJobStartState.Started, (await start).State);
            if (terminate is not null) Assert.Equal(scenario.Contains("terminal") ? JobTerminationState.Failed : JobTerminationState.CancellationRequested, (await terminate).State);
            await using var db = fixture.CreateDbContext();
            Assert.Equal(scenario is "request-first" or "start-first" or "terminal-first" ? 0 : scenario == "no-extension" ? 2 : 1,
                await db.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
            var job = await Job(s.Request.JobId);
            Assert.Equal(scenario.Contains("terminal") ? RequirementProcessingJobState.Failed : RequirementProcessingJobState.Running, job.State);
            if (scenario.Contains("start")) Assert.Equal(1, job.AttemptCount);
            if (scenario.Contains("request")) Assert.NotNull(job.CancellationRequestedAtUtc);
            if (scenario == "collision") Assert.Equal(otherBefore, System.Text.Json.JsonSerializer.Serialize(await Job(second.JobId)));
            if (!scenario.Contains("start") && !scenario.Contains("terminal")) Assert.Equal(before, await Snapshot(false));
        }
        finally
        {
            gate.Release.TrySetResult();
            if (firstRenew is not null) await firstRenew;
            if (secondRenew is not null) await secondRenew;
            if (start is not null) await start;
            if (terminate is not null) await terminate;
        }
    }

    [Theory]
    [InlineData("normal")] [InlineData("expired")] [InlineData("cancel")]
    public async Task Renew_Waiting_UsesGuardClock_NoExpiredRevival(string scenario)
    {
        var s = await RenewalSetup(); var e = s.Request;
        if (scenario == "expired")
        {
            var expiry = (await H.Now()).AddSeconds(3);
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={e.JobId}", Token);
            e = e with { ExpectedLeaseExpiresAtUtc = expiry };
        }
        var before = await Snapshot(); var writes = new Commands();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var holder = fixture.CreateDbContext();
        await using var tx = await holder.Database.BeginTransactionAsync(Token);
        await holder.PreQuotes.FromSqlInterpolated($"SELECT * FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE").ToListAsync(Token);
        Assert.True(await H.Now() < e.ExpectedLeaseExpiresAtUtc);
        var probe = new JobProtocolTestSupport.QuoteProbe();
        var task = RenewService(probe, writes).RenewActiveV2Async(e, s.Seed.Actor, caller.Token);
        try
        {
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var blocking = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
            await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocking);
            var floor = await H.Now();
            if (scenario == "expired")
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                // Actual DB clock observed while the competing transaction retains its lock.
                while (floor <= e.ExpectedLeaseExpiresAtUtc)
                { deadline.Token.ThrowIfCancellationRequested(); floor = await H.Now(); await Task.Yield(); }
            }
            output.WriteLine($"Renew wait {scenario}: PID {pid} blocked by {blocking}; DB clock {floor:O}, expected {e.ExpectedLeaseExpiresAtUtc:O}.");
            if (scenario == "cancel") caller.Cancel();
            await tx.RollbackAsync(Token);
            if (scenario == "cancel") Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
            else if (scenario == "expired")
            {
                var result = await task; Assert.Equal(LeaseOperationState.Rejected, result.State); Assert.Equal(LeaseOperationFailure.Expired, result.Failure);
                Assert.Equal(1, writes.Writes); Assert.Null(result.Receipt);
            }
            else { var result = await task; Assert.Equal(LeaseOperationState.Renewed, result.State); Assert.True(result.Receipt!.AppliedAtUtc >= floor); }
            if (scenario != "normal") Assert.Equal(before, await Snapshot());
        }
        finally { await tx.DisposeAsync(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Renew_PreCommitFailure_Rollback(bool cancel)
    {
        var s = await RenewalSetup(); var before = await Snapshot(); var injected = false;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var probe = new Commands { After = async (cmd, reader, _) =>
        {
            if (!cmd.CommandText.StartsWith("WITH clock")) return;
            injected = true; await reader.DisposeAsync(); if (cancel) caller.Cancel(); throw new IOException("after mutation");
        }};
        if (cancel) Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => RenewService(probe).RenewActiveV2Async(s.Request, s.Seed.Actor, caller.Token))).CancellationToken);
        else Assert.Equal(LeaseOperationState.PersistenceError, (await RenewService(probe).RenewActiveV2Async(s.Request, s.Seed.Actor, Token)).State);
        Assert.True(injected); Assert.Equal(1, probe.Writes); Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Renew_CommitLoss_IndependentObservation_OriginalCancellation(bool cancel, bool unavailable)
    {
        var s = await RenewalSetup(); var committed = false; var reached = Signal(); var release = Signal();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Guid? writer = null, observer = null; CancellationToken observationToken = default;
        var identities = new Contexts(id => { if (committed) observer = id; else writer = id; });
        var commands = new Commands { Before = async (cmd, ct) =>
        {
            if (!committed || !cmd.CommandText.Contains("pre_quotes") || !cmd.CommandText.Contains("FOR UPDATE")) return;
            observationToken = ct; reached.TrySetResult(); await release.Task.WaitAsync(ct);
            if (unavailable) throw new IOException("observer unavailable");
        }};
        var hook = new JobProtocolTestSupport.AfterCommit(() => { committed = true; throw new IOException("response loss"); });
        var task = RenewService(identities, commands, hook).RenewActiveV2Async(s.Request, s.Seed.Actor, caller.Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token); Assert.NotEqual(caller.Token, observationToken);
            if (cancel) caller.Cancel(); Assert.False(observationToken.IsCancellationRequested); release.TrySetResult();
            if (cancel) Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => task)).CancellationToken);
            else Assert.Equal(unavailable ? LeaseOperationState.Indeterminate : LeaseOperationState.AlreadyApplied, (await task).State);
            Assert.True(committed); Assert.NotNull(writer); Assert.NotNull(observer); Assert.NotEqual(writer, observer); Assert.Equal(1, commands.Writes);
            var before = await Snapshot(); var replayProbe = new Commands();
            Assert.Equal(LeaseOperationState.AlreadyApplied, (await RenewService(replayProbe).RenewActiveV2Async(s.Request, s.Seed.Actor, Token)).State);
            Assert.Equal(0, replayProbe.Writes); Assert.Equal(before, await Snapshot()); Assert.Null((await Job(s.Request.JobId)).CancellationRequestedAtUtc);
        }
        finally { release.TrySetResult(); try { await task; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData("absent")] [InlineData("different")] [InlineData("denied")] [InlineData("caller")]
    public async Task Renew_PostCommit_ExactCandidateOrOriginalCancellation(string scenario)
    {
        var s = await RenewalSetup(); var injected = false; using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            await using var db = fixture.CreateDbContext();
            if (scenario == "absent") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE operation_id={s.Request.OperationId}", Token);
            if (scenario == "different")
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET applied_at_utc=applied_at_utc+interval '1 microsecond',expires_at_utc=expires_at_utc+interval '1 microsecond' WHERE operation_id={s.Request.OperationId}", Token);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=lease_expires_at_utc+interval '1 microsecond' WHERE id={s.Request.JobId}", Token);
            }
            if (scenario == "denied") { (await db.Users.SingleAsync(Token)).Deactivate(await H.Now()); await db.SaveChangesAsync(Token); }
            injected = true;
            if (scenario == "caller") { caller.Cancel(); return; }
            throw new IOException("response lost");
        });
        var commands = new Commands();
        if (scenario == "caller") Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => RenewService(hook, commands).RenewActiveV2Async(s.Request, s.Seed.Actor, caller.Token))).CancellationToken);
        else
        {
            var result = await RenewService(hook, commands).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
            Assert.Equal(LeaseOperationState.Indeterminate, result.State); Assert.Null(result.Receipt); Assert.Null(result.Observation);
        }
        Assert.True(injected); Assert.Equal(1, commands.Writes);
        var job = await Job(s.Request.JobId); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.True(job.LeaseExpiresAtUtc > s.Request.ExpectedLeaseExpiresAtUtc); Assert.Null(job.CancellationRequestedAtUtc);
    }

    [Fact]
    public async Task Renew_ObservationBlocked_IndependentTimeout_NoCompensation()
    {
        var s = await RenewalSetup(); await using var holder = fixture.CreateDbContext(); await holder.Database.OpenConnectionAsync(Token);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? held = null;
        var committed = false; var probe = new JobProtocolTestSupport.QuoteProbe(() => committed); var writes = new Commands();
        var hook = new JobProtocolTestSupport.AfterCommit(async () =>
        {
            held = await holder.Database.BeginTransactionAsync(Token);
            await holder.PreQuotes.FromSqlInterpolated($"SELECT * FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE").ToListAsync(Token);
            committed = true; throw new IOException("response lost");
        });
        var task = RenewService(hook, probe, writes).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        try
        {
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token); var blocker = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
            await new JobProtocolTestSupport(fixture).WaitBlocked(pid, blocker);
            output.WriteLine($"Renew observer PID {pid} blocked by {blocker}; independent budget.");
            await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Equal(LeaseOperationState.Indeterminate, (await task).State);
        }
        finally { if (held is not null) await held.DisposeAsync(); await task; }
        Assert.Equal(1, writes.Writes); await using var read = fixture.CreateDbContext();
        Assert.Equal(2, await read.JobLeaseOperations.CountAsync(Token)); Assert.Null((await Job(s.Request.JobId)).CancellationRequestedAtUtc);
    }
}
