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
    private ObserveAssignedExecutionService Observer(params IInterceptor[] hooks) => new(new AssignedExecutionObservationStore(H.Options(hooks)));
    private static AssignedExecutionObservationRequest ObservationRequest(RenewalSeed s) =>
        new(s.Request.JobId, s.Request.RequirementId, s.Request.ExecutionRequestId, s.Request.ClaimOperationId);

    [Theory]
    [InlineData("claimed")] [InlineData("started")] [InlineData("requested")]
    [InlineData("expired")] [InlineData("failed")] [InlineData("failed-no-attempt")]
    [InlineData("cancelled")] [InlineData("cancelled-no-attempt")]
    [InlineData("successor")] [InlineData("inactive-context")]
    public async Task Observation_StatesAndHistory_IndependentRead_NoWrites(string mode)
    {
        var hasAttempt = mode is not ("claimed" or "failed-no-attempt" or "cancelled-no-attempt");
        var s = await RenewalSetup(hasAttempt, publicContext: true);
        if (mode is "requested" or "cancelled" or "cancelled-no-attempt")
            Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s.Seed)).State);
        if (mode is "cancelled" or "cancelled-no-attempt")
            Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Call("confirm", s.Seed)).State);
        if (mode is "failed" or "failed-no-attempt" or "successor")
            Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s.Seed)).State);
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "expired")
            {
                var expiry = (await Job(s.Request.JobId)).StartedAtUtc!.Value.AddTicks(10);
                Assert.True(await H.Now() > expiry);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={s.Request.JobId}", Token);
            }
            if (mode == "successor")
            {
                var old = await db.Requirements.SingleAsync(Token);
                var next = Requirement.Create(old.PreQuoteId, s.Seed.Actor, RequirementCommercialLine.Signature, await H.Now());
                old.SupersedeBy(next.Id, await H.Now()); db.Add(next);
            }
            if (mode == "inactive-context") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now());
            await db.SaveChangesAsync(Token);
        }
        var before = await Snapshot(); var commands = new Commands(); var saves = new ObservationNoSave();
        var lower = await H.Now();
        var result = await Observer(commands, saves).ObserveAsync(ObservationRequest(s), s.Seed.Actor, Token);
        var upper = await H.Now();
        Assert.Equal(AssignedExecutionObservationState.Observed, result.State);
        Assert.Equal(LeaseOperationFailure.None, result.Failure);
        var snap = Assert.IsType<AssignedExecutionSnapshot>(result.Snapshot); var job = await Job(s.Request.JobId);
        Assert.Equal(job.State, snap.JobState); Assert.Equal(job.ProcessingAttemptId, snap.ProcessingAttemptId);
        Assert.Equal(job.AttemptCount, snap.AttemptCount); Assert.Equal(hasAttempt ? 1 : 0, snap.AttemptCount);
        Assert.Equal(job.LeaseExpiresAtUtc, snap.LeaseExpiresAtUtc); Assert.Equal(job.LeaseOwner, snap.CurrentLeaseOwner);
        Assert.Equal(job.StartedAtUtc, snap.AssignedAtUtc); Assert.Equal(job.FinishedAtUtc, snap.FinishedAtUtc);
        Assert.Equal(job.CancellationRequestedAtUtc, snap.CancellationRequestedAtUtc);
        Assert.Equal(s.Request.ClaimOperationId, snap.ClaimOperationId); Assert.Equal(s.Request.LeaseOwner, snap.AssignedLeaseOwner);
        Assert.Equal(job.Id, snap.JobId); Assert.Equal(job.RequirementId, snap.RequirementId);
        Assert.Equal(job.ExecutionRequestId, snap.ExecutionRequestId); Assert.Equal(s.Seed.Actor, snap.ActorUserId);
        Assert.Equal(job.LeaseGeneration, snap.LeaseGeneration); Assert.InRange(snap.ObservedAtUtc, lower, upper);
        Assert.Equal(0, snap.ObservedAtUtc.Ticks % 10);
        var terminal = mode is "failed" or "failed-no-attempt" or "cancelled" or "cancelled-no-attempt" or "successor";
        Assert.Equal(terminal, snap.IsTerminal);
        Assert.Equal(terminal ? ObservedExecutionLease.Released : mode == "expired" ? ObservedExecutionLease.Expired : ObservedExecutionLease.Live, snap.Lease);
        Assert.Equal(mode is "successor" or "inactive-context" ? ObservedExecutionAuthorization.ContextChanged : ObservedExecutionAuthorization.ActiveContext, snap.Authorization);
        Assert.Equal(0, commands.Writes); Assert.Equal(0, saves.Calls); Assert.Equal(before, await Snapshot());
        // All read scopes must have ended before the snapshot reaches the caller.
        await using var locks = fixture.CreateDbContext(); await using var tx = await locks.Database.BeginTransactionAsync(Token);
        await locks.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE NOWAIT", Token);
        await locks.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirements WHERE id={s.Request.RequirementId} FOR UPDATE NOWAIT", Token);
        await locks.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirement_processing_jobs WHERE id={s.Request.JobId} FOR UPDATE NOWAIT", Token);
        await locks.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirement_processing_attempts WHERE requirement_id={s.Request.RequirementId} FOR UPDATE NOWAIT", Token);
        await locks.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.job_lease_operations WHERE operation_id={s.Request.ClaimOperationId} FOR UPDATE NOWAIT", Token);
        output.WriteLine($"Observation {mode}: exact snapshot, DML=0 SaveChanges=0; independent snapshot unchanged and locks released.");
    }

    [Theory]
    [InlineData("other", LeaseOperationFailure.NotFound)] [InlineData("admin", LeaseOperationFailure.NotFound)]
    [InlineData("inactive", LeaseOperationFailure.InactiveUser)]
    [InlineData("job", LeaseOperationFailure.NotFound)] [InlineData("claim-foreign", LeaseOperationFailure.ReceiptConflict)]
    [InlineData("request", LeaseOperationFailure.IdentityMismatch)] [InlineData("requirement", LeaseOperationFailure.IdentityMismatch)]
    [InlineData("claim", LeaseOperationFailure.ReceiptConflict)] [InlineData("claim-actor", LeaseOperationFailure.ReceiptConflict)]
    [InlineData("claim-renew", LeaseOperationFailure.ReceiptConflict)] [InlineData("owner", LeaseOperationFailure.IdentityMismatch)]
    [InlineData("correlation", LeaseOperationFailure.AttemptConflict)] [InlineData("foreign-attempt", LeaseOperationFailure.AttemptConflict)]
    [InlineData("finished", LeaseOperationFailure.AttemptConflict)] [InlineData("early", LeaseOperationFailure.AttemptConflict)]
    [InlineData("pending", LeaseOperationFailure.AttemptConflict)] [InlineData("links", LeaseOperationFailure.IdentityMismatch)]
    public async Task Observation_Rejected_NoOperationalDisclosure_NoDml(string mode, LeaseOperationFailure failure)
    {
        var s = await RenewalSetup(true, publicContext: true); var e = ObservationRequest(s); var actor = s.Seed.Actor;
        if (mode == "claim-renew")
        {
            var renew = await RenewService().RenewActiveV2Async(s.Request, actor, Token);
            Assert.Equal(LeaseOperationState.Renewed, renew.State); e = e with { ClaimOperationId = renew.Receipt!.OperationId };
        }
        var other = await OtherJob(s.Seed);
        if (mode == "claim-foreign")
        {
            var foreign = await ClaimForRenewal(other);
            e = e with { ClaimOperationId = foreign.Request.ClaimOperationId };
        }
        await using (var db = fixture.CreateDbContext())
        {
            if (mode is "other" or "admin" or "claim-actor")
            {
                var user = User.CreateFromGoogle("observer-other@test.local", "Other", null, null, await H.Now());
                if (mode == "admin") user.ChangeRole(UserRole.Admin, await H.Now());
                db.Add(user); await db.SaveChangesAsync(Token);
                if (mode == "claim-actor")
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET actor_user_id={user.Id} WHERE operation_id={e.ClaimOperationId}", Token);
                else actor = user.Id;
            }
            if (mode == "inactive") (await db.Users.SingleAsync(x => x.Id == actor, Token)).Deactivate(await H.Now());
            await db.SaveChangesAsync(Token);
            var id = s.Seed.Execution.ProcessingAttemptId;
            if (mode == "owner") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_owner={"another-executor"} WHERE id={e.JobId}", Token);
            if (mode == "correlation") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE id={id}", Token);
            if (mode == "foreign-attempt") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET requirement_id={other.Execution.RequirementId} WHERE id={id}", Token);
            if (mode == "pending") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET processing_state='Pending',started_at_utc=NULL WHERE id={id}", Token);
            if (mode == "early")
            {
                var at = (await Job(e.JobId)).StartedAtUtc!.Value.AddSeconds(-1);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET created_at_utc={at},started_at_utc={at} WHERE id={id}", Token);
            }
            if (mode == "finished")
            { (await db.RequirementProcessingAttempts.SingleAsync(x => x.Id == id, Token)).Fail("AI2_TIMEOUT", await H.Now()); await db.SaveChangesAsync(Token); }
            if (mode == "links") await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submissions SET requirement_id=NULL", Token);
        }
        e = mode switch { "job" => e with { JobId = Guid.NewGuid() }, "request" => e with { ExecutionRequestId = Guid.NewGuid() }, "requirement" => e with { RequirementId = other.Execution.RequirementId },
            "claim" => e with { ClaimOperationId = Guid.NewGuid() }, _ => e };
        var before = await Snapshot(); var probe = new Commands(); var saves = new ObservationNoSave();
        var result = await Observer(probe, saves).ObserveAsync(e, actor, Token);
        Assert.Equal(AssignedExecutionObservationState.Rejected, result.State); Assert.Equal(failure, result.Failure);
        Assert.Null(result.Snapshot); Assert.Equal(0, probe.Writes); Assert.Equal(0, saves.Calls); Assert.Equal(before, await Snapshot());
        output.WriteLine($"Observation {mode}: {failure}, no snapshot, DML=0, foreign entities preserved.");
    }

    [Fact]
    public async Task Observation_InvalidAndUnavailable_NoMutation_OriginalCancellation()
    {
        var s = await RenewalSetup(); var e = ObservationRequest(s); var opened = false;
        var invalid = await Observer(new OpenHook(() => opened = true)).ObserveAsync(e with { JobId = Guid.Empty }, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationFailure.InvalidRequest, invalid.Failure); Assert.False(opened); Assert.Null(invalid.Snapshot);
        var before = await Snapshot();
        var failed = await Observer(new OpenHook(() => throw new IOException("offline"))).ObserveAsync(e, s.Seed.Actor, Token);
        Assert.Equal(AssignedExecutionObservationState.Unavailable, failed.State); Assert.Null(failed.Snapshot);
        using var caller = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Observer(new OpenHook(() => { caller.Cancel(); throw new IOException("cancel"); }))
            .ObserveAsync(e, s.Seed.Actor, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Observation_CoherentWithConcurrentRequest_IndependentPids()
    {
        var s = await RenewalSetup(true); var release = Signal();
        var reached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Commands { Before = async (command, ct) =>
        {
            if (command.CommandText.Contains("job_lease_operations") && command.CommandText.Contains("FOR SHARE"))
            {
                reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
        }};
        var pending = Observer(probe).ObserveAsync(ObservationRequest(s), s.Seed.Actor, Token);
        Task<JobTerminationResult>? writer = null;
        try
        {
            var pid = await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var writerProbe = new JobProtocolTestSupport.QuoteProbe();
            writer = H.Call("request", s.Seed, H.Service(writerProbe));
            var blocked = await writerProbe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(pid, blocked);
            await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, pid);
            Assert.False(writer.IsCompleted); release.TrySetResult();
            var snapshot = (await pending).Snapshot!;
            Assert.Null(snapshot.CancellationRequestedAtUtc); Assert.Equal(ObservedExecutionLease.Live, snapshot.Lease);
            Assert.Equal(JobTerminationState.CancellationRequested, (await writer).State);
            Assert.NotNull((await Observer().ObserveAsync(ObservationRequest(s), s.Seed.Actor, Token)).Snapshot!.CancellationRequestedAtUtc);
            Assert.Equal(0, probe.Writes);
            output.WriteLine($"Observation coherent reader PID {pid}; Request blocked PID {blocked}; pg_blocking_pids observed; reader DML=0.");
        }
        finally { release.TrySetResult(); await pending; if (writer is not null) await writer; }
    }

    private sealed class ObservationNoSave : SaveChangesInterceptor
    {
        internal int Calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Observation cannot save."); }
    }
}
