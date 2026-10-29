using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed record ExpiredSeed(Seed Seed, ExpiredCancellation Input);
    private CloseExpiredCancellationService ExpiredCloser(params IInterceptor[] hooks) => new(new ExpiredCancellationStore(H.Options(hooks)));
    private async Task<ExpiredSeed> ExpiredSetup(Files files, bool attempt = true, bool request = true,
        bool expired = true, bool publicContext = false, bool evidence = false)
    {
        var s = await SeedFiles(files, publicContext, start: attempt);
        if (evidence)
        {
            var prepared = await Store(files, new Copies()).PrepareAsync(s.Execution, s.Actor, Token);
            Assert.Equal(ExecutionDocumentPreparationState.Registered, prepared.State);
            await prepared.Package!.DisposeAsync();
        }
        if (request) Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s)).State);
        var job = await CombinedJob();
        if (expired)
        {
            // Shorten only the current horizon, preserving all constraints and the real Claim receipt.
            await using var db = fixture.CreateDbContext();
            var expiry = job.StartedAtUtc!.Value.AddTicks(10);
            Assert.True(await H.Now() > expiry);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={job.Id}", Token);
            job = await CombinedJob();
        }
        await using var read = fixture.CreateDbContext();
        var claim = await read.JobLeaseOperations.SingleAsync(x => x.Kind == JobLeaseOperationKind.Claim, Token);
        return new(s, new(new(job.Id, job.RequirementId, job.ExecutionRequestId, claim.OperationId), Guid.NewGuid(),
            claim.LeaseOwner, 1, claim.AssignedAtUtc, job.ProcessingAttemptId,
            job.CancellationRequestedAtUtc ?? await H.Now(), job.LeaseExpiresAtUtc!.Value));
    }
    private async Task<string> ExpiredSnapshot()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new { all = await CombinedSnapshot(),
            receipts = await db.ExpiredExecutionCancellations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token),
            projects = await db.Projects.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive }).ToListAsync(Token) });
    }
    private static void ExpiredRejected(ExpiredClosureResult r, JobTerminationFailure failure)
    {
        Assert.Equal(JobTerminationState.Conflict, r.Result.State); Assert.Equal(failure, r.Result.Failure);
        Assert.Null(r.Receipt); Assert.Null(r.Result.Receipt);
    }

    [Theory]
    [InlineData(false, false, "active")] [InlineData(true, false, "active")]
    [InlineData(false, true, "project")] [InlineData(true, true, "project")]
    [InlineData(false, false, "client")] [InlineData(true, false, "client")]
    public async Task ExpiredB_ExactEffects_RealClaimRequest_Replay(bool attempt, bool publicContext, string context)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, attempt, publicContext: publicContext, evidence: attempt);
        await using (var db = fixture.CreateDbContext())
        {
            if (context == "project") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now());
            if (context == "client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
            await db.SaveChangesAsync(Token);
        }
        var immutable = await ClosureImmutable(); var jobBefore = await CombinedJob();
        await using var before = fixture.CreateDbContext();
        var reqBefore = await before.Requirements.AsNoTracking().SingleAsync(Token);
        var quoteBefore = await before.PreQuotes.AsNoTracking().SingleAsync(Token);
        var now = await H.Now();
        var result = await ExpiredCloser().CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.Result.State); Assert.Equal(JobTerminationFailure.None, result.Result.Failure);
        var receipt = Assert.IsType<ExpiredExecutionCancellation>(result.Receipt);
        Assert.InRange(receipt.FinishedAtUtc, now, await H.Now()); Assert.True(receipt.GuardAtUtc >= receipt.FinishedAtUtc);
        Assert.Equal(s.Input.OperationId, receipt.OperationId); Assert.Equal(s.Seed.Actor, receipt.ClosedByActorUserId);
        Assert.Equal(s.Input.ExpectedLeaseExpiresAtUtc, receipt.ExpectedLeaseExpiresAtUtc);
        Assert.Equal(s.Input.ExpectedCancellationRequestedAtUtc, receipt.CancellationRequestedAtUtc);
        Assert.Equal(s.Input.Identity.ClaimOperationId, receipt.ClaimOperationId);
        Assert.Equal(0, receipt.GuardAtUtc.Ticks % 10); Assert.Equal(0, receipt.FinishedAtUtc.Ticks % 10);
        var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Cancelled, job.State);
        Assert.Null(job.LeaseOwner); Assert.Null(job.LeaseExpiresAtUtc); Assert.Null(job.LastErrorCode);
        Assert.Null(job.PublicationContentHash); Assert.Null(job.PublicationContractVersion);
        Assert.Equal(jobBefore.RequiredPublicationVersion, job.RequiredPublicationVersion); Assert.Equal(jobBefore.StartedAtUtc, job.StartedAtUtc);
        Assert.Equal(jobBefore.CorrelationId, job.CorrelationId); Assert.Equal(jobBefore.ExecutionRequestId, job.ExecutionRequestId);
        Assert.Equal(jobBefore.RequestedByUserId, job.RequestedByUserId); Assert.Equal(jobBefore.LeaseGeneration, job.LeaseGeneration);
        Assert.Equal(jobBefore.AttemptCount, job.AttemptCount); Assert.Equal(jobBefore.ProcessingAttemptId, job.ProcessingAttemptId);
        Assert.Equal(receipt.FinishedAtUtc, job.FinishedAtUtc); Assert.Equal(receipt.CancellationRequestedAtUtc, job.CancellationRequestedAtUtc);
        await using var read = fixture.CreateDbContext();
        var req = await read.Requirements.AsNoTracking().SingleAsync(Token); var quote = await read.PreQuotes.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementStatus.Pending, req.Status); Assert.Equal(reqBefore.IsActive, req.IsActive);
        Assert.Equal(reqBefore.PreQuoteId, req.PreQuoteId); Assert.Equal(reqBefore.CreatedByUserId, req.CreatedByUserId);
        Assert.Equal(attempt ? receipt.FinishedAtUtc : reqBefore.UpdatedAtUtc, req.UpdatedAtUtc);
        Assert.Equal(attempt ? receipt.FinishedAtUtc : quoteBefore.UpdatedAtUtc, quote.UpdatedAtUtc);
        if (attempt)
        {
            var a = await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
            Assert.Equal(DocumentProcessingState.Finished, a.ProcessingState); Assert.Equal(DocumentProcessingOutcome.Cancelled, a.Outcome);
            Assert.Equal(receipt.FinishedAtUtc, a.CompletedAtUtc); Assert.Null(a.ErrorCode); Assert.Equal(job.ProcessingAttemptId, a.Id);
        }
        else Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.Equal(immutable, await ClosureImmutable());
        var snapshot = await ExpiredSnapshot(); var writes = new CombinedCommands();
        var replay = await ExpiredCloser(writes).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.AlreadyRecognized, replay.Result.State);
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(replay.Receipt));
        Assert.Equal(0, writes.Writes); Assert.Equal(snapshot, await ExpiredSnapshot()); Assert.Equal(0, files.Deletes);
        output.WriteLine($"B {attempt}/{publicContext}/{context}: exact effects, replay DML=0, snapshot unchanged.");
    }

    [Theory]
    [InlineData("no-request", JobTerminationFailure.CancellationRequired)]
    [InlineData("live", JobTerminationFailure.LeaseConflict)]
    [InlineData("expiry", JobTerminationFailure.LeaseConflict)]
    [InlineData("requested", JobTerminationFailure.IdentityConflict)]
    [InlineData("assignment", JobTerminationFailure.IdentityConflict)]
    [InlineData("request-id", JobTerminationFailure.IdentityConflict)]
    [InlineData("requirement-id", JobTerminationFailure.IdentityConflict)]
    [InlineData("claim-missing", JobTerminationFailure.IdentityConflict)]
    [InlineData("claim-renew", JobTerminationFailure.IdentityConflict)]
    [InlineData("claim-request", JobTerminationFailure.IdentityConflict)]
    [InlineData("owner", JobTerminationFailure.IdentityConflict)]
    [InlineData("attempt-id", JobTerminationFailure.AttemptConflict)]
    [InlineData("correlation", JobTerminationFailure.AttemptConflict)]
    [InlineData("early", JobTerminationFailure.AttemptConflict)]
    [InlineData("pending", JobTerminationFailure.AttemptConflict)]
    [InlineData("finished", JobTerminationFailure.AttemptConflict)]
    [InlineData("other-active", JobTerminationFailure.AttemptConflict)]
    [InlineData("not-current", JobTerminationFailure.StateConflict)]
    [InlineData("state", JobTerminationFailure.StateConflict)]
    [InlineData("extraction", JobTerminationFailure.InconsistentEvidence)]
    [InlineData("proposal", JobTerminationFailure.InconsistentEvidence)]
    [InlineData("inactive", JobTerminationFailure.InactiveUser)]
    [InlineData("other", JobTerminationFailure.NotFound)]
    [InlineData("admin", JobTerminationFailure.NotFound)]
    [InlineData("v1", JobTerminationFailure.ContextConflict)]
    public async Task ExpiredB_Rejections_ExactFailure_NoDisclosureOrDml(string mode, JobTerminationFailure failure)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, attempt: mode != "other-active", request: mode != "no-request", expired: mode != "live");
        var e = s.Input; var actor = s.Seed.Actor;
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "expiry") e = e with { ExpectedLeaseExpiresAtUtc = e.ExpectedLeaseExpiresAtUtc.AddTicks(10) };
            if (mode == "requested") e = e with { ExpectedCancellationRequestedAtUtc = e.ExpectedCancellationRequestedAtUtc.AddTicks(10) };
            if (mode == "assignment") e = e with { AssignedAtUtc = e.AssignedAtUtc.AddTicks(-10) };
            if (mode == "request-id") e = e with { Identity = e.Identity with { ExecutionRequestId = Guid.NewGuid() } };
            if (mode == "requirement-id") e = e with { Identity = e.Identity with { RequirementId = Guid.NewGuid() } };
            if (mode == "claim-missing") e = e with { Identity = e.Identity with { ClaimOperationId = Guid.NewGuid() } };
            if (mode == "claim-missing") e = e with { LeaseOwner = $"exec:{Guid.NewGuid():D}:{e.Identity.ClaimOperationId:D}" };
            if (mode == "claim-renew")
            {
                var c = await db.JobLeaseOperations.SingleAsync(Token);
                var renew = JobLeaseOperation.CreateRenew(Guid.NewGuid(), c.JobId, c.RequirementId, c.ExecutionRequestId, actor,
                    c.OperationId, c.LeaseOwner, 1, 1, c.ExpiresAtUtc, null, c.AssignedAtUtc, c.AssignedAtUtc, c.ExpiresAtUtc);
                db.Add(renew); await db.SaveChangesAsync(Token);
                e = e with { Identity = e.Identity with { ClaimOperationId = renew.OperationId }, LeaseOwner = $"exec:{Guid.NewGuid():D}:{renew.OperationId:D}" };
            }
            if (mode == "claim-request") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET execution_request_id={Guid.NewGuid()}", Token);
            if (mode == "owner") e = e with { LeaseOwner = $"exec:{Guid.NewGuid():D}:{e.Identity.ClaimOperationId:D}" };
            if (mode == "attempt-id") e = e with { ExpectedProcessingAttemptId = Guid.NewGuid() };
            if (mode == "correlation") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()}", Token);
            if (mode == "early") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET created_at_utc={e.AssignedAtUtc.AddTicks(-20)},started_at_utc={e.AssignedAtUtc.AddTicks(-10)}", Token);
            if (mode == "pending") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_attempts SET processing_state='Pending',started_at_utc=NULL", Token);
            if (mode == "finished") { (await db.RequirementProcessingAttempts.SingleAsync(Token)).Cancel(await H.Now()); await db.SaveChangesAsync(Token); }
            if (mode == "other-active") { db.Add(RequirementProcessingAttempt.Create(e.Identity.RequirementId, actor, Guid.NewGuid(), await H.Now())); await db.SaveChangesAsync(Token); }
            if (mode == "not-current") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET is_active=false", Token);
            if (mode == "state") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET status='Pending'", Token);
            if (mode is "extraction" or "proposal")
            {
                var x = RequirementExtractionResult.Create(e.ExpectedProcessingAttemptId!.Value, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, await H.Now());
                db.Add(x); if (mode == "proposal") db.Add(RequirementTechnicalProposal.Create(e.Identity.RequirementId, x.Id, e.ExpectedProcessingAttemptId.Value, false, await H.Now()));
                await db.SaveChangesAsync(Token);
            }
            if (mode == "inactive") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET is_active=false WHERE id={actor}", Token);
            if (mode is "other" or "admin")
            {
                var u = User.CreateFromGoogle("b-other@test.local", "Other", null, null, await H.Now());
                if (mode == "admin") u.ChangeRole(UserRole.Admin, await H.Now()); db.Add(u); await db.SaveChangesAsync(Token); actor = u.Id;
            }
            if (mode == "v1") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET required_publication_version='sng-job-publication-v1'", Token);
        }
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(e, actor, Token), failure);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot()); Assert.Equal(0, files.Opens); Assert.Equal(0, files.Deletes);
    }

    [Fact]
    public async Task ExpiredB_OwnerTransfer_ClosureNotAdoption_ActorReplayMismatch()
    {
        await using var files = new Files(); var s = await ExpiredSetup(files);
        Guid next;
        await using (var db = fixture.CreateDbContext())
        {
            var u = User.CreateFromGoogle("b-owner@test.local", "Next", null, null, await H.Now()); db.Add(u); await db.SaveChangesAsync(Token); next = u.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={next}", Token);
        }
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token), JobTerminationFailure.NotFound);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
        var immutable = await ClosureImmutable(); var result = await ExpiredCloser().CloseExpiredCancellationAsync(s.Input, next, Token);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.Result.State); Assert.Equal(next, result.Receipt!.ClosedByActorUserId);
        Assert.Equal(immutable, await ClosureImmutable());
        await using (var db = fixture.CreateDbContext()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={s.Seed.Actor}", Token);
        before = await ExpiredSnapshot(); writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token), JobTerminationFailure.IdentityConflict);
        var observed = await ClosureObserver().ObserveForClosureAsync(s.Input.Identity, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationFailure.None, observed.Failure); Assert.True(observed.Snapshot!.Execution.IsTerminal);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
    }

    [Theory]
    [InlineData("payload")] [InlineData("new-id")] [InlineData("failed")] [InlineData("confirm")]
    public async Task ExpiredB_TerminalAndReplay_NoAttributionToOtherClose(string mode)
    {
        await using var files = new Files(); var s = await ExpiredSetup(files, request: mode != "failed", expired: mode is "payload" or "new-id");
        if (mode is "payload" or "new-id") Assert.Equal(JobTerminationState.CancellationConfirmed, (await ExpiredCloser().CloseExpiredCancellationAsync(s.Input, s.Seed.Actor, Token)).Result.State);
        else Assert.Equal(mode == "failed" ? JobTerminationState.Failed : JobTerminationState.CancellationConfirmed, (await H.Call(mode == "failed" ? "fail" : "confirm", s.Seed)).State);
        var e = mode == "payload" ? s.Input with { ExpectedLeaseExpiresAtUtc = s.Input.ExpectedLeaseExpiresAtUtc.AddTicks(10) }
            : mode == "new-id" ? s.Input with { OperationId = Guid.NewGuid() } : s.Input;
        var before = await ExpiredSnapshot(); var writes = new CombinedCommands();
        ExpiredRejected(await ExpiredCloser(writes).CloseExpiredCancellationAsync(e, s.Seed.Actor, Token), mode == "payload" ? JobTerminationFailure.IdentityConflict : JobTerminationFailure.TerminalConflict);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await ExpiredSnapshot());
    }
}
