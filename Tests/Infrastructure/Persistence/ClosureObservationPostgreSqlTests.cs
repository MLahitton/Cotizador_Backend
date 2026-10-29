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

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    private ObserveForClosureService ClosureReader(params IInterceptor[] hooks) => new(new ExecutionClosureObservationStore(H.Options(hooks)));
    private static ExecutionClosureIdentity ClosureIdentity(RenewalSeed s) => new(s.Request.JobId,
        s.Request.RequirementId, s.Request.ExecutionRequestId, s.Request.ClaimOperationId);

    [Theory]
    [InlineData("claimed")] [InlineData("requested")] [InlineData("expired")]
    [InlineData("cancelled")] [InlineData("successor")] [InlineData("inactive-context")]
    public async Task ClosureA_Observation_History_NoDml_AllLocksReleased(string mode)
    {
        var s = await RenewalSetup(mode != "claimed", publicContext: true);
        if (mode is "requested" or "cancelled") await H.Call("request", s.Seed);
        if (mode == "cancelled") Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Call("confirm", s.Seed)).State);
        if (mode == "successor") Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s.Seed)).State);
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "successor")
            {
                var old = await db.Requirements.SingleAsync(Token);
                var next = Requirement.Create(old.PreQuoteId, s.Seed.Actor, RequirementCommercialLine.Signature, await H.Now());
                old.SupersedeBy(next.Id, await H.Now()); db.Add(next);
            }
            if (mode == "inactive-context") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now());
            await db.SaveChangesAsync(Token);
            if (mode == "expired")
            {
                var expiry = (await Job(s.Request.JobId)).StartedAtUtc!.Value.AddTicks(10);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={s.Request.JobId}", Token);
                Assert.True(await H.Now() > expiry);
            }
        }
        var before = await Snapshot(); var commands = new Commands(); var saves = new ObservationNoSave();
        var lower = await H.Now(); var result = await ClosureReader(commands, saves).ObserveForClosureAsync(ClosureIdentity(s), s.Seed.Actor, Token);
        var upper = await H.Now(); Assert.Equal(JobTerminationFailure.None, result.Failure);
        var snap = Assert.IsType<ClosureSnapshot>(result.Snapshot); var job = await Job(s.Request.JobId);
        Assert.Equal(job.State, snap.Execution.JobState); Assert.Equal(job.ProcessingAttemptId, snap.Execution.ProcessingAttemptId);
        Assert.Equal(job.LeaseExpiresAtUtc, snap.Execution.LeaseExpiresAtUtc); Assert.Equal(job.FinishedAtUtc, snap.Execution.FinishedAtUtc);
        Assert.Equal(job.CancellationRequestedAtUtc, snap.Execution.CancellationRequestedAtUtc);
        Assert.Equal(s.Seed.Actor, snap.ClaimActorUserId); Assert.Equal(s.Seed.Actor, snap.Execution.ActorUserId);
        Assert.InRange(snap.Execution.ObservedAtUtc, lower, upper); Assert.Equal(0, snap.Execution.ObservedAtUtc.Ticks % 10);
        Assert.Equal(mode is "successor" or "cancelled" ? ObservedExecutionLease.Released
            : mode == "expired" ? ObservedExecutionLease.Expired : ObservedExecutionLease.Live, snap.Execution.Lease);
        Assert.Equal(mode is "successor" or "inactive-context" ? ObservedExecutionAuthorization.ContextChanged
            : ObservedExecutionAuthorization.ActiveContext, snap.Execution.Authorization);
        Assert.Equal(0, commands.Writes); Assert.Equal(0, saves.Calls); Assert.Equal(before, await Snapshot());
        await using var locks = fixture.CreateDbContext(); await using var tx = await locks.Database.BeginTransactionAsync(Token);
        // All protocol rows, including the authority rows, are unlocked before return.
        await locks.Database.ExecuteSqlRawAsync("""
            SELECT 1 FROM identity.users FOR UPDATE NOWAIT;
            SELECT 1 FROM core.clients FOR UPDATE NOWAIT;
            SELECT 1 FROM core.projects FOR UPDATE NOWAIT;
            SELECT 1 FROM core.pre_quotes FOR UPDATE NOWAIT;
            SELECT 1 FROM core.requirements FOR UPDATE NOWAIT;
            SELECT 1 FROM core.requirement_processing_jobs FOR UPDATE NOWAIT;
            SELECT 1 FROM core.requirement_processing_attempts FOR UPDATE NOWAIT;
            SELECT 1 FROM core.job_lease_operations FOR UPDATE NOWAIT;
            SELECT 1 FROM core.public_experience_submissions FOR UPDATE NOWAIT;
            """, Token);
        output.WriteLine($"Closure observation {mode}: independent snapshots unchanged, DML=0, SaveChanges=0, all locks released.");
    }

    [Theory]
    [InlineData("job", JobTerminationFailure.NotFound)]
    [InlineData("requirement", JobTerminationFailure.IdentityConflict)]
    [InlineData("request", JobTerminationFailure.IdentityConflict)]
    [InlineData("claim", JobTerminationFailure.InconsistentEvidence)]
    [InlineData("renew", JobTerminationFailure.InconsistentEvidence)]
    [InlineData("correlation", JobTerminationFailure.AttemptConflict)]
    [InlineData("foreign-attempt", JobTerminationFailure.AttemptConflict)]
    [InlineData("pending", JobTerminationFailure.AttemptConflict)]
    [InlineData("early", JobTerminationFailure.AttemptConflict)]
    [InlineData("finished", JobTerminationFailure.AttemptConflict)]
    [InlineData("admin", JobTerminationFailure.NotFound)]
    [InlineData("inactive", JobTerminationFailure.InactiveUser)]
    [InlineData("requirement-state", JobTerminationFailure.StateConflict)]
    public async Task ClosureA_Observation_ExactRejection_NoDisclosure(string mode, JobTerminationFailure failure)
    {
        var s = await RenewalSetup(true); var e = ClosureIdentity(s); var actor = s.Seed.Actor;
        var other = await OtherJob(s.Seed);
        if (mode == "renew")
        {
            var renew = await RenewService().RenewActiveV2Async(s.Request, actor, Token);
            Assert.Equal(LeaseOperationState.Renewed, renew.State); e = e with { ClaimOperationId = renew.Receipt!.OperationId };
        }
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "admin")
            {
                var user = User.CreateFromGoogle("closure-admin@test.local", "Other", null, null, await H.Now());
                user.ChangeRole(UserRole.Admin, await H.Now()); db.Add(user); actor = user.Id;
            }
            if (mode == "inactive") (await db.Users.SingleAsync(x => x.Id == actor, Token)).Deactivate(await H.Now());
            await db.SaveChangesAsync(Token);
            var id = s.Seed.Execution.ProcessingAttemptId;
            if (mode == "requirement-state") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirements SET status='Pending' WHERE id={e.RequirementId}", Token);
            if (mode == "correlation") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE id={id}", Token);
            if (mode == "foreign-attempt") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET requirement_id={other.Execution.RequirementId} WHERE id={id}", Token);
            if (mode == "pending") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET processing_state='Pending',started_at_utc=NULL WHERE id={id}", Token);
            if (mode == "early")
            {
                var at = (await Job(e.JobId)).StartedAtUtc!.Value.AddSeconds(-1);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET created_at_utc={at},started_at_utc={at} WHERE id={id}", Token);
            }
            if (mode == "finished") { (await db.RequirementProcessingAttempts.SingleAsync(x => x.Id == id, Token)).Fail("AI2_TIMEOUT", await H.Now()); await db.SaveChangesAsync(Token); }
        }
        e = mode switch { "job" => e with { JobId = Guid.NewGuid() }, "request" => e with { ExecutionRequestId = Guid.NewGuid() },
            "requirement" => e with { RequirementId = other.Execution.RequirementId }, "claim" => e with { ClaimOperationId = Guid.NewGuid() }, _ => e };
        var before = await Snapshot(); var writes = new Commands(); var saves = new ObservationNoSave();
        var result = await ClosureReader(writes, saves).ObserveForClosureAsync(e, actor, Token);
        Assert.Equal(failure, result.Failure); Assert.Null(result.Snapshot); Assert.Equal(0, writes.Writes);
        Assert.Equal(0, saves.Calls); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task ClosureA_Observation_CoherentWithRequest_IndependentPids()
    {
        var s = await RenewalSetup(true); var release = Signal();
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Commands { Before = async (command, ct) =>
        {
            if (command.CommandText.Contains("job_lease_operations") && command.CommandText.Contains("FOR SHARE"))
            { reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID); await release.Task.WaitAsync(ct); }
        }};
        var pending = ClosureReader(probe).ObserveForClosureAsync(ClosureIdentity(s), s.Seed.Actor, Token);
        Task<JobTerminationResult>? writer = null;
        try
        {
            var pid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            var writerProbe = new JobProtocolTestSupport.QuoteProbe(); writer = H.Call("request", s.Seed, H.Service(writerProbe));
            var blocked = await writerProbe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(pid, blocked); await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, pid);
            release.TrySetResult(); Assert.Null((await pending).Snapshot!.Execution.CancellationRequestedAtUtc);
            Assert.Equal(JobTerminationState.CancellationRequested, (await writer).State);
            Assert.NotNull((await ClosureReader().ObserveForClosureAsync(ClosureIdentity(s), s.Seed.Actor, Token)).Snapshot!.Execution.CancellationRequestedAtUtc);
            Assert.Equal(0, probe.Writes); output.WriteLine($"Closure observation reader {pid}/Request {blocked}: pg_blocking_pids observed.");
        }
        finally { release.TrySetResult(); await pending; if (writer is not null) await writer; }
    }
}
