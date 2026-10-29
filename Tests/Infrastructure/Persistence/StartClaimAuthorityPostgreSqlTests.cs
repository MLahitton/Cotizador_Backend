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
    private async Task<Guid> NextStartOwner(bool transfer)
    {
        await using var db = fixture.CreateDbContext();
        var user = User.CreateFromGoogle("start-owner@test.local", "Next", null, null, await H.Now());
        db.Add(user); await db.SaveChangesAsync(Token);
        // There is no transfer API: change the actual FK with all constraints enabled.
        if (transfer) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={user.Id}", Token);
        return user.Id;
    }

    [Fact]
    public async Task StartClaim_H1_NewOwnerFromClosureSnapshot_RejectedWithoutWrites()
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var next = await NextStartOwner(true);
        var observed = await ClosureObserver().ObserveForClosureAsync(
            new(s.Receipt.JobId, s.Receipt.RequirementId, s.Receipt.ExecutionRequestId, s.Receipt.OperationId), next, Token);
        Assert.Equal(JobTerminationFailure.None, observed.Failure);
        Assert.Equal(s.Seed.Actor, observed.Snapshot!.ClaimActorUserId);
        var x = observed.Snapshot.Execution;
        var e = new AssignedJobExecution(x.JobId, x.ExecutionRequestId, x.RequirementId, x.AssignedLeaseOwner, x.LeaseGeneration, x.ProcessingAttemptId);
        var job = await CombinedJob();
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.ProcessingAttemptId); Assert.Equal(0, job.AttemptCount);
        Assert.True(job.LeaseExpiresAtUtc > await H.Now());
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var result = await H.StartService(writes).StartVerifiedAsync(e, next, Token);
        output.WriteLine($"H1 direct Start: {result.State}/{result.Failure}; DML={writes.Writes}; attempt={(await CombinedJob()).ProcessingAttemptId}");
        Assert.Equal(AssignedJobStartState.Rejected, result.State);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, result.Failure); Assert.Null(result.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        var direct = new AssignedJobStartStore(H.Options(writes), RequirementProcessingJob.PublicationV2);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, (await direct.StartAsync(e, next, Token)).Failure);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, (await direct.RecognizeAsync(e, next, Token)).Failure);
        Assert.Equal(AssignedJobStartFailure.StateConflict, (await H.StartService(writes).StartAsync(e, next, Token)).Failure);
        Assert.Equal(AssignedJobStartFailure.StateConflict, (await H.StartService(writes).RecognizeAsync(e, next, Token)).Failure);
        Assert.Equal(AssignedJobStartFailure.NotFound, (await H.StartService(writes).StartVerifiedAsync(e, s.Seed.Actor, Token)).Failure);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(0, files.Opens); Assert.Equal(0, files.Deletes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task StartClaim_CompatibleStartAndReplay_TransferCannotRecognize(bool terminal, bool transfer)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var immutable = await ClosureImmutable();
        var started = await H.StartService().StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.Started, started.State);
        var e = s.Seed.Execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
        await using (var db = fixture.CreateDbContext())
        {
            var attempt = Assert.Single(await db.RequirementProcessingAttempts.AsNoTracking().ToListAsync(Token));
            Assert.Equal(e.ProcessingAttemptId, attempt.Id); Assert.Equal(s.Seed.Actor, attempt.RequestedByUserId);
            Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState);
            Assert.Equal(RequirementStatus.Processing, (await db.Requirements.SingleAsync(Token)).Status);
        }
        if (terminal) Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s.Seed with { Execution = e })).State);
        var actor = transfer ? await NextStartOwner(true) : s.Seed.Actor;
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var store = new AssignedJobStartStore(H.Options(writes), RequirementProcessingJob.PublicationV2);
        foreach (var result in new[] { await store.StartVerifiedAsync(e, actor, Token),
            await store.StartAsync(e, actor, Token), await store.RecognizeAsync(e, actor, Token) })
        {
            Assert.Equal(transfer ? AssignedJobStartState.Rejected : AssignedJobStartState.AlreadyStarted, result.State);
            Assert.Equal(transfer ? AssignedJobStartFailure.IdentityConflict : AssignedJobStartFailure.None, result.Failure);
            if (transfer) Assert.Null(result.Receipt); else Assert.Equal(started.Receipt, result.Receipt);
        }
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(immutable, await ClosureImmutable()); Assert.Equal(0, files.Opens);
    }

    [Theory]
    [InlineData("missing")] [InlineData("actor")] [InlineData("request")]
    [InlineData("assignment")] [InlineData("owner")] [InlineData("requirement")] [InlineData("job")]
    public async Task StartClaim_InvalidReceipt_ExactRejection_NoWrites(string mode)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var otherActor = await NextStartOwner(false);
        await using (var db = fixture.CreateDbContext())
        {
            // Relational inconsistencies are not produced by Claim. SQL fixtures keep every constraint active.
            var id = s.Receipt.OperationId;
            if (mode == "missing") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE operation_id={id}", Token);
            if (mode == "actor") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET actor_user_id={otherActor} WHERE operation_id={id}", Token);
            if (mode == "request") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET execution_request_id={Guid.NewGuid()} WHERE operation_id={id}", Token);
            if (mode == "assignment") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET assigned_at_utc={s.Receipt.AssignedAtUtc.AddSeconds(-1)},applied_at_utc={s.Receipt.AppliedAtUtc.AddSeconds(-1)},expires_at_utc={s.Receipt.ExpiresAtUtc.AddSeconds(-1)} WHERE operation_id={id}", Token);
            if (mode == "owner") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET lease_owner={$"exec:{Guid.NewGuid():D}:{id:D}"} WHERE operation_id={id}", Token);
            if (mode is "requirement" or "job")
            {
                var quote = PreQuote.Create((await db.PreQuotes.SingleAsync(Token)).ProjectId, s.Seed.Actor, "PC-2026-0002", null, await H.Now());
                var other = Requirement.Create(quote.Id, s.Seed.Actor, RequirementCommercialLine.Essential, await H.Now());
                var otherJob = RequirementProcessingJob.CreateForVerifiedPublication(other.Id, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, await H.Now());
                db.AddRange(quote, other, otherJob); await db.SaveChangesAsync(Token);
                if (mode == "requirement") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET requirement_id={other.Id} WHERE operation_id={id}", Token);
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET job_id={otherJob.Id} WHERE operation_id={id}", Token);
                    // A Renew for this job and actor is not a substitute for its own Claim.
                    db.Add(JobLeaseOperation.CreateRenew(Guid.NewGuid(), s.Receipt.JobId, s.Receipt.RequirementId,
                        s.Receipt.ExecutionRequestId, s.Seed.Actor, id, s.Receipt.LeaseOwner, 1, 1,
                        s.Receipt.ExpiresAtUtc, null, s.Receipt.AssignedAtUtc, s.Receipt.AppliedAtUtc.AddSeconds(1), s.Receipt.ExpiresAtUtc.AddSeconds(1)));
                    await db.SaveChangesAsync(Token);
                }
            }
        }
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var result = await H.StartService(writes).StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.Rejected, result.State);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, result.Failure); Assert.Null(result.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        await using var read = fixture.CreateDbContext(); Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        output.WriteLine($"Start Claim {mode}: IdentityConflict, DML=0, independent snapshot unchanged.");
    }

    [Fact]
    public async Task StartClaim_NewOwnerCanRequestAndCloseWithoutAdopting()
    {
        await using var files = new Files(); var s = await ClosureSetup(files, false, request: false);
        var next = await NextStartOwner(true);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, (await H.StartService().StartVerifiedAsync(s.Seed.Execution, next, Token)).Failure);
        var immutable = await ClosureImmutable();
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s.Seed with { Actor = next })).State);
        var observed = await ClosureObserver().ObserveForClosureAsync(s.Input.Identity, next, Token);
        Assert.Equal(JobTerminationFailure.None, observed.Failure);
        var input = s.Input with { ExpectedCancellationRequestedAtUtc = observed.Snapshot!.Execution.CancellationRequestedAtUtc!.Value };
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await Closer().ConfirmStoppedCancellationAsync(input, s.Drain, next, Token)).State);
        Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
        Assert.Equal(immutable, await ClosureImmutable());
    }

    private sealed class StartProjectProbe : DbCommandInterceptor
    {
        internal TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("core.projects") && command.CommandText.Contains("FOR SHARE"))
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartClaim_OwnerTransferRace_PostgreSqlOrder(bool startFirst)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var next = await NextStartOwner(false);
        var immutable = await ClosureImmutable(); var before = await CombinedSnapshot();
        var reached = Signal(); var release = Signal(); var probe = new StartProjectProbe();
        var writes = new CombinedCommands();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync(Token);
        await using var tx = await connection.BeginTransactionAsync(Token);
        await using var command = connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "UPDATE core.projects SET created_by_user_id=@actor";
        command.Parameters.AddWithValue("actor", next);
        Task<AssignedJobStartResult>? start = null; Task<int>? transfer = null; var committed = false;
        try
        {
            if (!startFirst) Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
            var hooks = startFirst ? new IInterceptor[] { probe, writes, new Saved(async _ => { reached.TrySetResult(); await release.Task.WaitAsync(Token); }) }
                : new IInterceptor[] { probe, writes };
            start = H.StartService(hooks).StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
            var pid = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(pid, connection.ProcessID);
            if (startFirst)
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
                transfer = command.ExecuteNonQueryAsync(Token);
                await new JobProtocolTestSupport(fixture).WaitBlocked(connection.ProcessID, pid);
                release.TrySetResult();
                Assert.Equal(AssignedJobStartState.Started, (await start).State);
                Assert.Equal(1, await transfer); await tx.CommitAsync(Token); committed = true;
            }
            else
            {
                await new JobProtocolTestSupport(fixture).WaitBlocked(pid, connection.ProcessID);
                await tx.CommitAsync(Token); committed = true;
                var result = await start;
                Assert.Equal(AssignedJobStartState.Rejected, result.State); Assert.Equal(AssignedJobStartFailure.NotFound, result.Failure);
                Assert.Null(result.Receipt); Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
            }
            output.WriteLine($"Start ownership race startFirst={startFirst}: Start PID={pid}, transfer PID={connection.ProcessID}, pg_blocking_pids observed.");
        }
        finally
        {
            release.TrySetResult();
            try
            {
                if (startFirst && start is not null) await start;
                if (transfer is not null) await transfer;
            }
            finally
            {
                if (!committed) await tx.RollbackAsync(Token);
                if (start is not null) await start;
            }
        }
        var job = await CombinedJob(); Assert.Equal(startFirst ? 1 : 0, job.AttemptCount);
        await using var read = fixture.CreateDbContext();
        var attempts = await read.RequirementProcessingAttempts.AsNoTracking().ToListAsync(Token);
        if (startFirst) Assert.Equal(s.Seed.Actor, Assert.Single(attempts).RequestedByUserId); else Assert.Empty(attempts);
        Assert.Equal(next, (await read.Projects.SingleAsync(Token)).CreatedByUserId);
        Assert.Equal(immutable, await ClosureImmutable()); Assert.Equal(0, files.Opens);
    }
}
