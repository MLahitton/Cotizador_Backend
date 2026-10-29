using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobTerminationPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private JobTerminationTestSupport H => new(fixture);

    private static void AssertUnpublishedVersion(RequirementProcessingJob before, RequirementProcessingJob after, bool verified)
    {
        Assert.Equal(verified ? RequirementProcessingJob.PublicationV2 : RequirementProcessingJob.PublicationV1, after.RequiredPublicationVersion);
        Assert.Equal(before.RequiredPublicationVersion, after.RequiredPublicationVersion);
        Assert.Null(after.PublicationContractVersion);
        Assert.Null(after.PublicationContentHash);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.ExecutionRequestId, after.ExecutionRequestId);
        Assert.Equal(before.RequirementId, after.RequirementId);
        Assert.Equal(before.CorrelationId, after.CorrelationId);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(before.LeaseGeneration, after.LeaseGeneration);
    }

    private async Task SeedUnrelatedHistory(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        var at = (await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == s.Execution.RequirementId, Token)).CreatedAtUtc;
        var projectId = (await db.PreQuotes.AsNoTracking().SingleAsync(x => x.Id == s.Quote, Token)).ProjectId;
        var quote = PreQuote.Create(projectId, s.Actor, "PC-2026-0002", null, at);
        var requirement = Requirement.Create(quote.Id, s.Actor, RequirementCommercialLine.Essential, at);
        const string hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var file = RequirementFile.CreateWithStoredEvidence(requirement.Id, "control.pdf", "application/pdf", 3,
            "tests/terminal-control-not-opened.pdf", at, hash, 3, at, 1);
        var job = RequirementProcessingJob.Create(requirement.Id, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, at);
        job.AssignExecution("control", at, at.AddMinutes(10));
        var attempt = RequirementProcessingAttempt.Create(requirement.Id, s.Actor, job.CorrelationId, at);
        attempt.Start(at);
        job.LinkStartedAttempt(attempt.Id);
        requirement.StartProcessing(at);
        attempt.Complete(DocumentProcessingOutcome.Completed, at);
        requirement.MarkProcessed(at);
        job.Succeed(at);
        var extraction = RequirementExtractionResult.Create(attempt.Id, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, at);
        var proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, attempt.Id, false, at);
        // Structural control history, not evidence of a provider call or a new byte verification.
        var id = Guid.NewGuid();
        var evidence = RequirementExecutionEvidence.Create(id, requirement.Id, job.Id, job.ExecutionRequestId,
            attempt.Id, 1, s.Actor, file.Id, 1, at, at, 1,
            new[] { RequirementExecutionDocument.Create(id, file.Id, 0, file.OriginalFileName, file.ContentType,
                file.StorageKey, at, 3, hash, 3, at, 1, hash, 3) });
        db.AddRange(quote, requirement, file, job, attempt, extraction, proposal, evidence);
        await db.SaveChangesAsync(Token);
    }

    private async Task<string> ProtectedSnapshot(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            files = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token),
            extractions = await db.RequirementExtractionResults.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            proposals = await db.RequirementTechnicalProposals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            jobs = await db.RequirementProcessingJobs.AsNoTracking().Where(x => x.Id != s.Execution.JobId).OrderBy(x => x.Id).ToListAsync(Token),
            attempts = await db.RequirementProcessingAttempts.AsNoTracking().Where(x => x.RequirementId != s.Execution.RequirementId).OrderBy(x => x.Id).ToListAsync(Token),
            requirements = await db.Requirements.AsNoTracking().Where(x => x.Id != s.Execution.RequirementId).OrderBy(x => x.Id).ToListAsync(Token),
            quotes = await db.PreQuotes.AsNoTracking().Where(x => x.Id != s.Quote).OrderBy(x => x.Id).ToListAsync(Token)
        });
    }

    [Theory]
    [InlineData("queued", false)]
    [InlineData("assigned", false)]
    [InlineData("started", false)]
    [InlineData("expired", false)]
    [InlineData("queued", true)]
    public async Task Request_OnlyJobChanges_AndReplayPreservesFirstDate(string scenario, bool verifiedPublication)
    {
        var s = await H.Create(running: scenario != "queued", attempt: scenario == "started", verifiedPublication: verifiedPublication);
        await SeedUnrelatedHistory(s);
        var protectedBefore = await ProtectedSnapshot(s);
        if (scenario == "expired") await Expire(s);
        var related = await H.Snapshot(false);
        await using var db = fixture.CreateDbContext();
        var before = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Execution.JobId, Token);
        var result = await H.Call("request", s);
        Assert.Equal(scenario == "queued" ? JobTerminationState.CancellationConfirmed : JobTerminationState.CancellationRequested, result.State);
        var after = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Execution.JobId, Token);
        AssertUnpublishedVersion(before, after, verifiedPublication);
        if (scenario == "queued")
        {
            Assert.Equal(RequirementProcessingJobState.Cancelled, after.State);
            Assert.Equal(after.CancellationRequestedAtUtc, after.FinishedAtUtc);
            Assert.Null(after.StartedAtUtc);
            Assert.Null(after.ProcessingAttemptId);
            Assert.Equal(0, after.AttemptCount);
            Assert.Empty(await db.RequirementProcessingAttempts.Where(x => x.RequirementId == s.Execution.RequirementId).ToListAsync(Token));
        }
        Assert.NotNull(after.CancellationRequestedAtUtc);
        Assert.Equal(before.ProcessingAttemptId, after.ProcessingAttemptId);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.StartedAtUtc, after.StartedAtUtc);
        Assert.Equal(before.LeaseOwner, after.LeaseOwner);
        Assert.Equal(before.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Equal(related, await H.Snapshot(false));
        Assert.Equal(protectedBefore, await ProtectedSnapshot(s));
        var snapshot = await H.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var replay = await H.Call("request", s, H.Service(writes));
        Assert.Equal(JobTerminationState.AlreadyRecognized, replay.State);
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(0, writes.Count);
        Assert.Equal(protectedBefore, await ProtectedSnapshot(s));
        Assert.Equal(snapshot, await H.Snapshot());
        if (scenario == "expired")
        {
            Assert.Equal(JobTerminationFailure.LeaseConflict, (await H.Call("confirm", s)).Failure);
            Assert.Equal(snapshot, await H.Snapshot());
        }
    }

    [Theory]
    [InlineData("confirm", false, false)]
    [InlineData("confirm", true, false)]
    [InlineData("fail", false, false)]
    [InlineData("fail", true, false)]
    [InlineData("confirm", false, true)]
    [InlineData("confirm", true, true)]
    [InlineData("fail", false, true)]
    [InlineData("fail", true, true)]
    public async Task Finalize_AtomicWithAndWithoutAttempt_AndTerminalReplay(string operation, bool hasAttempt, bool verifiedPublication)
    {
        var s = await H.Create(attempt: hasAttempt, request: operation == "confirm", verifiedPublication: verifiedPublication);
        await SeedUnrelatedHistory(s);
        var protectedBefore = await ProtectedSnapshot(s);
        var related = await H.Snapshot(false);
        await using var db = fixture.CreateDbContext();
        var before = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Execution.JobId, Token);
        var requirementBefore = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == s.Execution.RequirementId, Token);
        var attemptBefore = await db.RequirementProcessingAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.RequirementId == s.Execution.RequirementId, Token);
        var result = await H.Call(operation, s);
        Assert.Equal(operation == "confirm" ? JobTerminationState.CancellationConfirmed : JobTerminationState.Failed, result.State);
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == s.Execution.JobId, Token);
        AssertUnpublishedVersion(before, job, verifiedPublication);
        Assert.Equal(hasAttempt ? 1 : 0, job.AttemptCount);
        Assert.Equal(s.Execution.ProcessingAttemptId, job.ProcessingAttemptId);
        var requirement = await db.Requirements.AsNoTracking().SingleAsync(x => x.Id == s.Execution.RequirementId, Token);
        Assert.Equal(requirementBefore.IsActive, requirement.IsActive);
        Assert.Equal(requirementBefore.CreatedAtUtc, requirement.CreatedAtUtc);
        Assert.Equal(requirementBefore.SupersededByRequirementId, requirement.SupersededByRequirementId);
        Assert.Equal(requirementBefore.SupersedesRequirementId, requirement.SupersedesRequirementId);
        Assert.Equal(operation == "confirm" ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Failed, job.State);
        Assert.Equal(before.CancellationRequestedAtUtc, job.CancellationRequestedAtUtc);
        Assert.Equal(before.StartedAtUtc, job.StartedAtUtc);
        Assert.Equal(before.LeaseGeneration, job.LeaseGeneration);
        Assert.Equal(before.AttemptCount, job.AttemptCount);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresAtUtc);
        Assert.NotNull(job.FinishedAtUtc);
        if (hasAttempt)
        {
            var attempt = await db.RequirementProcessingAttempts.AsNoTracking().SingleAsync(x => x.RequirementId == s.Execution.RequirementId, Token);
            Assert.NotNull(attemptBefore);
            Assert.Equal(attemptBefore.Id, attempt.Id);
            Assert.Equal(attemptBefore.CreatedAtUtc, attempt.CreatedAtUtc);
            Assert.Equal(attemptBefore.StartedAtUtc, attempt.StartedAtUtc);
            Assert.Equal(attemptBefore.CorrelationId, attempt.CorrelationId);
            Assert.Equal(attemptBefore.RequestedByUserId, attempt.RequestedByUserId);
            Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
            Assert.Equal(operation == "confirm" ? DocumentProcessingOutcome.Cancelled : DocumentProcessingOutcome.Failed, attempt.Outcome);
            Assert.Equal(job.LastErrorCode, attempt.ErrorCode);
            Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
            Assert.Equal(job.FinishedAtUtc, (await db.PreQuotes.AsNoTracking().SingleAsync(x => x.Id == s.Quote, Token)).UpdatedAtUtc);
            Assert.Equal(job.FinishedAtUtc, requirement.UpdatedAtUtc);
            Assert.Equal(operation == "confirm" ? RequirementStatus.Pending : RequirementStatus.Failed,
                requirement.Status);
        }
        else
        {
            Assert.Null(attemptBefore);
            Assert.Empty(await db.RequirementProcessingAttempts.Where(x => x.RequirementId == s.Execution.RequirementId).ToListAsync(Token));
            Assert.Equal(related, await H.Snapshot(false));
        }
        Assert.Equal(protectedBefore, await ProtectedSnapshot(s));
        var snapshot = await H.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var exactReplay = await H.Call(operation, s, H.Service(writes));
        Assert.Equal(JobTerminationState.AlreadyRecognized, exactReplay.State);
        Assert.Equal(result.Receipt, exactReplay.Receipt);
        Assert.Equal(0, writes.Count);
        Assert.Equal(snapshot, await H.Snapshot());
        Assert.Equal(protectedBefore, await ProtectedSnapshot(s));
        var replay = await H.Call(operation, s with { Execution = s.Execution with { LeaseOwner = "released-owner" } });
        Assert.Equal(JobTerminationState.AlreadyRecognized, replay.State);
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(snapshot, await H.Snapshot());
        if (operation == "fail")
        {
            Assert.Equal(JobTerminationFailure.IdentityConflict,
                (await H.Service().FinalizeJobFailedAsync(s.Execution, s.Actor, "AI_INVALID_RESPONSE", Token)).Failure);
            Assert.Equal(JobTerminationFailure.TerminalConflict, (await H.Call("request", s)).Failure);
            Assert.Equal(snapshot, await H.Snapshot());
        }
    }

    public static IEnumerable<object[]> RejectionCases()
    {
        foreach (var op in new[] { "request", "confirm", "fail" })
            foreach (var scenario in new[] { "other", "admin", "inactive", "missingUser", "requestId", "requirementId", "publicLinks" })
                yield return [op, scenario];
        foreach (var op in new[] { "confirm", "fail" })
            foreach (var scenario in new[] { "owner", "generation", "attemptId", "missingAttempt", "expired", "notCurrent", "wrongRequirementState", "correlation" })
                yield return [op, scenario];
        yield return ["confirm", "noRequest"];
        yield return ["fail", "requested"];
        yield return ["fail", "inactiveProject"];
        yield return ["fail", "inactiveClient"];
        yield return ["fail", "unsafeError"];
    }

    [Theory]
    [MemberData(nameof(RejectionCases))]
    public async Task Rejections_DoNotChangeAnyJobAttemptRequirementOrActivity(string operation, string scenario)
    {
        var s = await H.Create(attempt: true, request: operation == "confirm" && scenario != "noRequest");
        await using (var db = fixture.CreateDbContext())
        {
            var now = await H.Now();
            switch (scenario)
            {
                case "other":
                case "admin":
                    var other = global::Domain.Identity.User.CreateFromGoogle("other@example.test", "Other", null, null, now);
                    if (scenario == "admin") other.ChangeRole(global::Domain.Identity.UserRole.Admin, now);
                    db.Add(other); s = s with { Actor = other.Id }; break;
                case "inactive": (await db.Users.SingleAsync(Token)).Deactivate(now); break;
                case "missingUser": s = s with { Actor = Guid.NewGuid() }; break;
                case "requestId": s = s with { Execution = s.Execution with { ExecutionRequestId = Guid.NewGuid() } }; break;
                case "requirementId": s = s with { Execution = s.Execution with { RequirementId = Guid.NewGuid() } }; break;
                case "owner": s = s with { Execution = s.Execution with { LeaseOwner = "stale" } }; break;
                case "generation": s = s with { Execution = s.Execution with { LeaseGeneration = 2 } }; break;
                case "attemptId": s = s with { Execution = s.Execution with { ProcessingAttemptId = Guid.NewGuid() } }; break;
                case "missingAttempt": s = s with { Execution = s.Execution with { ProcessingAttemptId = null } }; break;
                case "expired": await Expire(s); break;
                case "requested": await H.Call("request", s); break;
                case "inactiveProject": (await db.Projects.SingleAsync(Token)).SetActive(false, s.Actor, now); break;
                case "inactiveClient": (await db.Clients.SingleAsync(Token)).SetActive(false, s.Actor, now); break;
                case "wrongRequirementState": (await db.Requirements.SingleAsync(Token)).MarkProcessingCancelled(now); break;
                case "notCurrent":
                    var r = await db.Requirements.SingleAsync(Token);
                    r.MarkFailed(now);
                    var replacement = Requirement.Create(s.Quote, s.Actor, RequirementCommercialLine.Essential, now);
                    replacement.MarkAsReplacementOf(r.Id); r.SupersedeBy(replacement.Id, now); db.Add(replacement); break;
                case "correlation":
                    var correlation = Guid.NewGuid();
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id = {correlation} WHERE id = {s.Execution.ProcessingAttemptId}", Token); break;
                case "publicLinks":
                    var submission = CotizadorBackend.Tests.Application.PublicExperience.ProcessPublicExperienceSubmissionTests.Submission();
                    db.Add(submission); await db.SaveChangesAsync(Token);
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET submission_id = {submission.Id} WHERE id = {s.Execution.JobId}", Token); break;
            }
            await db.SaveChangesAsync(Token);
        }
        var before = await H.Snapshot();
        var result = scenario == "unsafeError"
            ? await H.Service().FinalizeJobFailedAsync(s.Execution, s.Actor, "exception: credential=secret", Token)
            : await H.Call(operation, s);
        Assert.Equal(JobTerminationState.Conflict, result.State);
        Assert.NotEqual(JobTerminationFailure.PersistenceError, result.Failure);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await H.Snapshot());
    }

    [Fact]
    public async Task Cancellation_DoesNotRequireActiveClientOrProject()
    {
        var s = await H.Create(attempt: true);
        await using (var db = fixture.CreateDbContext())
        {
            var now = await H.Now();
            (await db.Projects.SingleAsync(Token)).SetActive(false, s.Actor, now);
            (await db.Clients.SingleAsync(Token)).SetActive(false, s.Actor, now);
            await db.SaveChangesAsync(Token);
        }
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s)).State);
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Call("confirm", s)).State);
    }

    [Fact]
    public async Task SucceededJob_RequestCannotUndoIt()
    {
        var s = await H.Create(attempt: true);
        await using (var db = fixture.CreateDbContext())
        {
            var now = await H.Now();
            (await db.RequirementProcessingAttempts.SingleAsync(Token)).Complete(DocumentProcessingOutcome.Completed, now);
            (await db.RequirementProcessingJobs.SingleAsync(Token)).Succeed(now);
            (await db.Requirements.SingleAsync(Token)).MarkProcessed(now);
            await db.SaveChangesAsync(Token);
        }
        var before = await H.Snapshot();
        Assert.Equal(JobTerminationFailure.TerminalConflict, (await H.Call("request", s)).Failure);
        Assert.Equal(before, await H.Snapshot());
    }

    [Fact]
    public async Task StaleExecution_NeverTouchesSuccessor_OrCurrentRequirement()
    {
        var s = await H.Create(attempt: true, request: true);
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Call("confirm", s)).State);
        await using (var db = fixture.CreateDbContext())
        {
            var now = await H.Now();
            var next = RequirementProcessingJob.Create(s.Execution.RequirementId, Guid.NewGuid(), Guid.NewGuid(),
                RequirementProcessingJobTrigger.ManualRetry, now, retryOfJobId: s.Execution.JobId);
            next.AssignExecution("next", now, now.AddMinutes(10)); db.Add(next);
            await db.SaveChangesAsync(Token);
            var started = await H.StartService().StartAsync(new(next.Id, next.ExecutionRequestId, next.RequirementId, "next", 1), s.Actor, Token);
            Assert.Equal(AssignedJobStartState.Started, started.State);
        }
        var before = await H.Snapshot();
        Assert.Equal(JobTerminationFailure.TerminalConflict, (await H.Call("fail", s)).Failure);
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await H.Call("confirm", s)).State);
        Assert.Equal(before, await H.Snapshot());
    }

    private async Task Expire(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        var expired = (await H.Now()).AddSeconds(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {expired} WHERE id = {s.Execution.JobId}", Token);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("fail")]
    public async Task LinkedAttemptFromAnotherRequirement_IsNotAdopted(string operation)
    {
        var s = await H.Create(request: operation == "confirm");
        await using (var db = fixture.CreateDbContext())
        {
            var now = await H.Now();
            var project = await db.Projects.SingleAsync(Token);
            var quote = PreQuote.Create(project.Id, s.Actor, "PC-2026-0002", null, now);
            var requirement = Requirement.Create(quote.Id, s.Actor, RequirementCommercialLine.Essential, now);
            var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
            var foreign = RequirementProcessingAttempt.Create(requirement.Id, s.Actor, job.CorrelationId, now);
            foreign.Start(now);
            db.AddRange(quote, requirement, foreign);
            await db.SaveChangesAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET processing_attempt_id = {foreign.Id}, attempt_count = 1 WHERE id = {job.Id}", Token);
            s = s with { Execution = s.Execution with { ProcessingAttemptId = foreign.Id } };
        }
        var before = await H.Snapshot();
        Assert.Equal(JobTerminationFailure.AttemptConflict, (await H.Call(operation, s)).Failure);
        Assert.Equal(before, await H.Snapshot());
    }
}
