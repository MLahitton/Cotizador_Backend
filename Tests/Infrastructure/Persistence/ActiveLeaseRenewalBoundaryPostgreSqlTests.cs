using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    [Fact]
    public async Task Renew_NonemptyGraphAndClaimPreserved_NoCommercialEffects()
    {
        var s = await RenewalSetup(attempt: true); var control = await OtherJob(s.Seed);
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(x => x.Id == control.Execution.JobId, Token);
            var requirement = await db.Requirements.SingleAsync(x => x.Id == job.RequirementId, Token); var at = job.CreatedAtUtc;
            const string hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
            var file = RequirementFile.CreateWithStoredEvidence(requirement.Id, "control.pdf", "application/pdf", 3, "tests/not-opened", at, hash, 3, at, 1);
            job.AssignExecution("structural-control", at, at.AddMinutes(10));
            var attempt = RequirementProcessingAttempt.Create(requirement.Id, s.Seed.Actor, job.CorrelationId, at);
            attempt.Start(at); job.LinkStartedAttempt(attempt.Id); requirement.StartProcessing(at);
            attempt.Complete(DocumentProcessingOutcome.Completed, at); requirement.MarkProcessed(at);
            job.SucceedWithPublicationReceipt(RequirementProcessingJob.PublicationV2, new string('A', 64), at);
            var extraction = RequirementExtractionResult.Create(attempt.Id, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, at);
            var proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, attempt.Id, false, at);
            var id = Guid.NewGuid();
            var evidence = RequirementExecutionEvidence.Create(id, requirement.Id, job.Id, job.ExecutionRequestId, attempt.Id,
                1, s.Seed.Actor, file.Id, 1, at, at, 1, new[] { RequirementExecutionDocument.Create(id, file.Id, 0,
                    file.OriginalFileName, file.ContentType, file.StorageKey, at, 3, hash, 3, at, 1, hash, 3) });
            db.AddRange(file, attempt, extraction, proposal, evidence); await db.SaveChangesAsync(Token);
        }
        var before = await Snapshot(false); var otherJob = JsonSerializer.Serialize(await Job(control.Execution.JobId));
        string claimBefore;
        await using (var read = fixture.CreateDbContext()) claimBefore = JsonSerializer.Serialize(await read.JobLeaseOperations.AsNoTracking().SingleAsync(Token));
        Assert.Equal(LeaseOperationState.Renewed, (await RenewService().RenewActiveV2Async(s.Request, s.Seed.Actor, Token)).State);
        Assert.Equal(before, await Snapshot(false)); Assert.Equal(otherJob, JsonSerializer.Serialize(await Job(control.Execution.JobId)));
        await using var check = fixture.CreateDbContext();
        Assert.Equal(claimBefore, JsonSerializer.Serialize(await check.JobLeaseOperations.AsNoTracking().SingleAsync(x => x.OperationId == s.Request.ClaimOperationId, Token)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Renew_P5_NoExtensionAfterRequest_ConfirmOnlyLive(bool expired)
    {
        var s = await RenewalSetup(attempt: true);
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", s.Seed)).State);
        if (expired)
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=started_at_utc+interval '1 microsecond' WHERE id={s.Request.JobId}", Token);
            Assert.True((await Job(s.Request.JobId)).LeaseExpiresAtUtc < await H.Now());
        }
        var before = await Snapshot(); var writes = new Commands();
        var denied = await RenewService(writes).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Rejected, denied.State); Assert.Equal(LeaseOperationFailure.CancellationRequested, denied.Failure);
        Assert.Null(denied.Receipt); Assert.Null(denied.Observation); Assert.Equal(0, writes.Writes); Assert.Equal(before, await Snapshot());
        var confirmWrites = new Commands();
        var confirm = await H.Call("confirm", s.Seed, H.Service(confirmWrites));
        if (expired)
        {
            Assert.Equal(JobTerminationState.Conflict, confirm.State); Assert.Equal(JobTerminationFailure.LeaseConflict, confirm.Failure);
            Assert.Equal(0, confirmWrites.Writes); Assert.Equal(before, await Snapshot());
            Assert.Equal(RequirementProcessingJobState.Running, (await Job(s.Request.JobId)).State);
        }
        else
        {
            Assert.Equal(JobTerminationState.CancellationConfirmed, confirm.State);
            Assert.Equal(RequirementProcessingJobState.Cancelled, (await Job(s.Request.JobId)).State);
        }
        await using var read = fixture.CreateDbContext(); Assert.Single(await read.JobLeaseOperations.ToListAsync(Token));
    }

    [Fact]
    public async Task Renew_TerminalAttemptAndSuccessor_ReplayPreservesHistory()
    {
        var s = await RenewalSetup(attempt: true);
        var original = await RenewService().RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Renewed, original.State);
        Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s.Seed)).State);
        await using (var db = fixture.CreateDbContext())
        {
            var old = await db.Requirements.SingleAsync(Token); var now = await H.Now();
            var next = Requirement.Create(old.PreQuoteId, s.Seed.Actor, RequirementCommercialLine.Essential, now);
            old.SupersedeBy(next.Id, now); next.MarkAsReplacementOf(old.Id); db.Add(next); await db.SaveChangesAsync(Token);
        }
        var before = await Snapshot(); var writes = new Commands();
        var replay = await RenewService(writes).RenewActiveV2Async(s.Request, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State); Assert.Equal(LeaseAvailability.Terminal, replay.Observation!.Availability);
        Assert.Equal(JsonSerializer.Serialize(original.Receipt), JsonSerializer.Serialize(replay.Receipt));
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Renew_FutureAssignment_ZeroGuardRows_NoReceipt()
    {
        var s = await RenewalSetup(); var future = (await H.Now()).AddHours(1);
        await using (var db = fixture.CreateDbContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET started_at_utc={future},lease_expires_at_utc={future.AddSeconds(120)} WHERE id={s.Request.JobId}", Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET assigned_at_utc={future},applied_at_utc={future},expires_at_utc={future.AddSeconds(120)} WHERE operation_id={s.Request.ClaimOperationId}", Token);
            await tx.CommitAsync(Token);
        }
        var before = await Snapshot(); var commands = new Commands();
        var result = await RenewService(commands).RenewActiveV2Async(s.Request with { ExpectedLeaseExpiresAtUtc = future.AddSeconds(120) }, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Conflict, result.State); Assert.Equal(LeaseOperationFailure.IdentityMismatch, result.Failure);
        Assert.Null(result.Receipt); Assert.Null(result.Observation); Assert.Equal(1, commands.Writes); Assert.Equal(before, await Snapshot());
    }
}
