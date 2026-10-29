using System.Text.Json;
using System.Text.Json.Nodes;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    private RenewActiveLeaseService RenewService(params IInterceptor[] hooks) => new(new ActiveLeaseRenewalStore(H.Options(hooks)));
    private sealed record RenewalSeed(Seed Seed, RenewActiveLease Request);
    private async Task<RenewalSeed> RenewalSetup(bool attempt = false, bool noExtension = false, bool publicContext = false)
    {
        var s = await H.Create(running: false, publicContext: publicContext, verifiedPublication: true);
        return await ClaimForRenewal(s, attempt, noExtension);
    }
    private async Task<RenewalSeed> ClaimForRenewal(Seed s, bool attempt = false, bool noExtension = false)
    {
        var c = await Service().ClaimInitialV2Async(Request(s), s.Actor, Token);
        Assert.Equal(LeaseOperationState.Claimed, c.State);
        var receipt = c.Receipt!;
        var execution = s.Execution with { LeaseOwner = receipt.LeaseOwner };
        if (attempt)
        {
            var started = await H.StartService().StartVerifiedAsync(execution, s.Actor, Token);
            Assert.Equal(AssignedJobStartState.Started, started.State);
            execution = execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
        }
        var expiry = receipt.ExpiresAtUtc;
        if (noExtension)
        {
            // Valid SQL fixture for a longer current horizon, not a previously executed Renew.
            expiry = (await H.Now()).AddMinutes(10);
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={execution.JobId}", Token);
        }
        return new(s with { Execution = execution }, new(execution.JobId, execution.RequirementId, execution.ExecutionRequestId,
            Guid.NewGuid(), receipt.OperationId, receipt.LeaseOwner, 1, expiry, execution.ProcessingAttemptId));
    }
    private static string ExceptExpiry(RequirementProcessingJob job)
    {
        var json = JsonSerializer.SerializeToNode(job)!.AsObject(); json.Remove(nameof(job.LeaseExpiresAtUtc)); return json.ToJsonString();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Renew_FormulaAndExactEffects_OriginalReplay(bool attempt, bool noExtension)
    {
        var s = await RenewalSetup(attempt, noExtension, publicContext: attempt);
        var before = await Snapshot(false); var jobBefore = ExceptExpiry(await Job(s.Request.JobId));
        var probe = new Commands(); var lower = await H.Now();
        var result = await RenewService(probe).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        var upper = await H.Now();
        Assert.Equal(LeaseOperationState.Renewed, result.State);
        var receipt = Assert.IsType<JobLeaseOperation>(result.Receipt);
        Assert.InRange(receipt.AppliedAtUtc, lower, upper); Assert.Equal(0, receipt.AppliedAtUtc.Ticks % 10);
        Assert.Equal(noExtension ? s.Request.ExpectedLeaseExpiresAtUtc : receipt.AppliedAtUtc.AddSeconds(120), receipt.ExpiresAtUtc);
        Assert.Equal(!noExtension, receipt.IsEffectiveExtension);
        Assert.Equal(s.Request.ExpectedLeaseExpiresAtUtc, receipt.ExpectedLeaseExpiresAtUtc);
        Assert.Equal(s.Request.ExpectedProcessingAttemptId, receipt.ExpectedProcessingAttemptId);
        Assert.Equal(s.Request.OperationId, receipt.OperationId); Assert.Equal(s.Request.ClaimOperationId, receipt.ClaimOperationId);
        Assert.Equal(receipt.ExpiresAtUtc, (await Job(s.Request.JobId)).LeaseExpiresAtUtc);
        Assert.Equal(jobBefore, ExceptExpiry(await Job(s.Request.JobId))); Assert.Equal(before, await Snapshot(false)); Assert.Equal(1, probe.Writes);
        var snapshot = await Snapshot(); var replayProbe = new Commands();
        var replay = await RenewService(replayProbe).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State);
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(replay.Receipt));
        Assert.Equal(0, replayProbe.Writes); Assert.Equal(snapshot, await Snapshot());
        await using var read = fixture.CreateDbContext(); Assert.Equal(2, await read.JobLeaseOperations.CountAsync(Token));
        Assert.False(read.Database.HasPendingModelChanges());
        output.WriteLine($"Renew attempt={attempt} noExtension={noExtension}: Applied={receipt.AppliedAtUtc:O}, Expected={receipt.ExpectedLeaseExpiresAtUtc:O}, Expires={receipt.ExpiresAtUtc:O}; replay DML=0.");
    }

    [Theory]
    [InlineData("actor")] [InlineData("job")] [InlineData("requirement")] [InlineData("request")]
    [InlineData("operation")] [InlineData("claim")] [InlineData("same-id")] [InlineData("attempt")]
    [InlineData("owner")] [InlineData("generation")] [InlineData("offset")] [InlineData("precision")]
    public async Task Renew_InvalidInput_NoConnection(string field)
    {
        var claim = Guid.NewGuid(); var actor = Guid.NewGuid();
        var e = new RenewActiveLease(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), claim,
            $"exec:{Guid.NewGuid():D}:{claim:D}", 1, DateTimeOffset.UnixEpoch, null);
        e = field switch {
            "job" => e with { JobId = Guid.Empty }, "requirement" => e with { RequirementId = Guid.Empty },
            "request" => e with { ExecutionRequestId = Guid.Empty }, "operation" => e with { OperationId = Guid.Empty },
            "claim" => e with { ClaimOperationId = Guid.Empty }, "same-id" => e with { OperationId = claim },
            "attempt" => e with { ExpectedProcessingAttemptId = Guid.Empty }, "owner" => e with { LeaseOwner = "executor" },
            "generation" => e with { LeaseGeneration = 2 }, "offset" => e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.ToOffset(TimeSpan.FromHours(1)) },
            "precision" => e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.AddTicks(1) }, _ => e };
        if (field == "actor") actor = Guid.Empty;
        var opened = false;
        var result = await RenewService(new OpenHook(() => opened = true)).RenewActiveV2Async(e, actor, Token);
        Assert.Equal(LeaseOperationState.Rejected, result.State); Assert.Equal(LeaseOperationFailure.InvalidRequest, result.Failure);
        Assert.False(opened); Assert.Null(result.Receipt); Assert.Null(result.Observation);
    }

    [Theory]
    [InlineData("other", LeaseOperationFailure.NotFound)] [InlineData("admin", LeaseOperationFailure.NotFound)]
    [InlineData("inactive-user", LeaseOperationFailure.InactiveUser)]
    [InlineData("inactive-client", LeaseOperationFailure.InactiveContext)] [InlineData("inactive-project", LeaseOperationFailure.InactiveContext)]
    [InlineData("request", LeaseOperationFailure.IdentityMismatch)] [InlineData("requirement", LeaseOperationFailure.IdentityMismatch)]
    [InlineData("owner", LeaseOperationFailure.ReceiptConflict)] [InlineData("claim-missing", LeaseOperationFailure.ReceiptConflict)]
    [InlineData("claim-foreign", LeaseOperationFailure.ReceiptConflict)] [InlineData("claim-renew", LeaseOperationFailure.ReceiptConflict)]
    [InlineData("claim-actor", LeaseOperationFailure.ReceiptConflict)] [InlineData("v1", LeaseOperationFailure.WrongVersion)]
    [InlineData("not-current", LeaseOperationFailure.IdentityMismatch)] [InlineData("state", LeaseOperationFailure.AttemptConflict)]
    [InlineData("active-attempt", LeaseOperationFailure.AttemptConflict)] [InlineData("expected-attempt", LeaseOperationFailure.AttemptConflict)]
    [InlineData("correlation", LeaseOperationFailure.AttemptConflict)] [InlineData("foreign-attempt", LeaseOperationFailure.AttemptConflict)]
    [InlineData("early-attempt", LeaseOperationFailure.AttemptConflict)] [InlineData("finished-attempt", LeaseOperationFailure.AttemptConflict)]
    [InlineData("cancel", LeaseOperationFailure.CancellationRequested)] [InlineData("terminal", LeaseOperationFailure.Terminal)]
    [InlineData("stale", LeaseOperationFailure.StaleLease)] [InlineData("expired", LeaseOperationFailure.Expired)]
    public async Task Renew_Rejections_ExactFailure_NoDisclosureOrEffects(string scenario, LeaseOperationFailure expected)
    {
        var withAttempt = scenario is "expected-attempt" or "correlation" or "foreign-attempt" or "early-attempt" or "finished-attempt";
        var s = await RenewalSetup(withAttempt); var e = s.Request; var actor = s.Seed.Actor; var now = await H.Now();
        if (scenario == "cancel") await H.Call("request", s.Seed);
        if (scenario == "terminal") await H.Call("fail", s.Seed);
        if (scenario == "claim-foreign")
        {
            var other = await ClaimForRenewal(await OtherJob(s.Seed)); e = e with { ClaimOperationId = other.Request.ClaimOperationId, LeaseOwner = other.Request.LeaseOwner };
        }
        if (scenario == "claim-renew")
        {
            Assert.Equal(LeaseOperationState.Renewed, (await RenewService().RenewActiveV2Async(e, actor, Token)).State);
            e = e with { ClaimOperationId = e.OperationId, OperationId = Guid.NewGuid(), LeaseOwner = $"exec:{Guid.NewGuid():D}:{e.OperationId:D}" };
        }
        var foreign = scenario == "foreign-attempt" ? await OtherJob(s.Seed) : null;
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario is "other" or "admin" or "claim-actor")
            {
                var user = User.CreateFromGoogle("renew-other@test.local", "Other", null, null, now);
                if (scenario == "admin") user.ChangeRole(UserRole.Admin, now);
                db.Add(user); await db.SaveChangesAsync(Token);
                if (scenario == "claim-actor") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET actor_user_id={user.Id} WHERE operation_id={e.ClaimOperationId}", Token);
                else actor = user.Id;
            }
            if (scenario == "inactive-user") (await db.Users.SingleAsync(Token)).Deactivate(now);
            if (scenario == "inactive-client") (await db.Clients.SingleAsync(Token)).SetActive(false, actor, now);
            if (scenario == "inactive-project") (await db.Projects.SingleAsync(Token)).SetActive(false, actor, now);
            if (scenario == "not-current") (await db.Requirements.SingleAsync(Token)).Cancel(now);
            if (scenario == "state") (await db.Requirements.SingleAsync(Token)).StartProcessing(now);
            if (scenario == "active-attempt") db.Add(RequirementProcessingAttempt.Create(e.RequirementId, actor, Guid.NewGuid(), now));
            if (scenario == "finished-attempt") (await db.RequirementProcessingAttempts.SingleAsync(Token)).Fail("AI2_TIMEOUT", now);
            await db.SaveChangesAsync(Token);
            if (scenario == "v1") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET required_publication_version={RequirementProcessingJob.PublicationV1} WHERE id={e.JobId}", Token);
            if (scenario == "correlation") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE id={e.ExpectedProcessingAttemptId}", Token);
            if (scenario == "foreign-attempt") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET requirement_id={foreign!.Execution.RequirementId} WHERE id={e.ExpectedProcessingAttemptId}", Token);
            if (scenario == "early-attempt") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_attempts SET created_at_utc=created_at_utc-interval '5 minutes',started_at_utc=started_at_utc-interval '5 minutes'", Token);
            if (scenario == "expired")
            {
                await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET started_at_utc=started_at_utc-interval '5 minutes',lease_expires_at_utc=lease_expires_at_utc-interval '5 minutes'", Token);
                await db.Database.ExecuteSqlRawAsync("UPDATE core.job_lease_operations SET assigned_at_utc=assigned_at_utc-interval '5 minutes',applied_at_utc=applied_at_utc-interval '5 minutes',expires_at_utc=expires_at_utc-interval '5 minutes'", Token);
                e = e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.AddMinutes(-5) };
            }
        }
        e = scenario switch {
            "request" => e with { ExecutionRequestId = Guid.NewGuid() }, "requirement" => e with { RequirementId = Guid.NewGuid() },
            "owner" => e with { LeaseOwner = $"exec:{Guid.NewGuid():D}:{e.ClaimOperationId:D}" },
            "stale" => e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.AddSeconds(1) },
            "expected-attempt" => e with { ExpectedProcessingAttemptId = null }, _ => e };
        if (scenario == "claim-missing") { var id = Guid.NewGuid(); e = e with { ClaimOperationId = id, LeaseOwner = $"exec:{Guid.NewGuid():D}:{id:D}" }; }
        var before = await Snapshot(); var probe = new Commands();
        var result = await RenewService(probe).RenewActiveV2Async(e, actor, Token);
        var rejected = expected is LeaseOperationFailure.NotFound or LeaseOperationFailure.InactiveUser or LeaseOperationFailure.InactiveContext
            or LeaseOperationFailure.WrongVersion or LeaseOperationFailure.CancellationRequested or LeaseOperationFailure.Terminal or LeaseOperationFailure.Expired;
        Assert.Equal(rejected ? LeaseOperationState.Rejected : LeaseOperationState.Conflict, result.State);
        Assert.Equal(expected, result.Failure); Assert.Null(result.Receipt); Assert.Null(result.Observation);
        Assert.Equal(scenario == "expired" ? 1 : 0, probe.Writes); Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData("payload")] [InlineData("later-renew")] [InlineData("expired")] [InlineData("terminal")]
    [InlineData("cancel")] [InlineData("started-later")] [InlineData("incompatible-attempt")] [InlineData("inactive-context")]
    public async Task Renew_HistoricalReplay_SeparatedObservation(string scenario)
    {
        var s = await RenewalSetup(); var original = await RenewService().RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Renewed, original.State); var e = s.Request;
        if (scenario == "later-renew") Assert.Equal(LeaseOperationState.Renewed, (await RenewService().RenewActiveV2Async(
            e with { OperationId = Guid.NewGuid(), ExpectedLeaseExpiresAtUtc = original.Receipt!.ExpiresAtUtc }, s.Seed.Actor, Token)).State);
        if (scenario == "terminal") Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s.Seed)).State);
        if (scenario == "cancel") await H.Call("request", s.Seed);
        if (scenario is "started-later" or "incompatible-attempt")
        {
            var start = await H.StartService().StartVerifiedAsync(s.Seed.Execution, s.Seed.Actor, Token);
            Assert.Equal(AssignedJobStartState.Started, start.State);
            if (scenario == "incompatible-attempt")
            {
                await using var db = fixture.CreateDbContext();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE id={start.Receipt!.ProcessingAttemptId}", Token);
            }
        }
        if (scenario == "expired")
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=started_at_utc+interval '1 microsecond' WHERE id={e.JobId}", Token);
            Assert.True((await Job(e.JobId)).LeaseExpiresAtUtc < await H.Now());
        }
        if (scenario == "inactive-context") { await using var db = fixture.CreateDbContext(); (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now()); await db.SaveChangesAsync(Token); }
        if (scenario == "payload") e = e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.AddSeconds(1) };
        var before = await Snapshot(); var probe = new Commands();
        var replay = await RenewService(probe).RenewActiveV2Async(e, s.Seed.Actor, Token);
        if (scenario is "payload" or "incompatible-attempt")
        {
            Assert.Equal(LeaseOperationState.Conflict, replay.State);
            Assert.Equal(scenario == "payload" ? LeaseOperationFailure.ReceiptConflict : LeaseOperationFailure.AttemptConflict, replay.Failure);
            Assert.Null(replay.Receipt); Assert.Null(replay.Observation);
        }
        else
        {
            Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State);
            Assert.Equal(JsonSerializer.Serialize(original.Receipt), JsonSerializer.Serialize(replay.Receipt));
            Assert.Equal(scenario switch { "later-renew" => LeaseAvailability.Superseded, "expired" => LeaseAvailability.Expired,
                "terminal" => LeaseAvailability.Terminal, "cancel" => LeaseAvailability.CancellationRequested,
                "inactive-context" => LeaseAvailability.ContextChanged, _ => LeaseAvailability.Live }, replay.Observation!.Availability);
        }
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await Snapshot());
    }
}
