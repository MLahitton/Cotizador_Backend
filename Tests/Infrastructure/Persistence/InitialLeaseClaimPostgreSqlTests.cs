using System.Data.Common;
using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class InitialLeaseClaimPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobTerminationTestSupport H => new(fixture);
    private ClaimInitialLeaseService Service(params IInterceptor[] hooks) => new(new InitialLeaseClaimStore(H.Options(hooks)));
    private static ClaimInitialLease Request(Seed s) => new(s.Execution.JobId, s.Execution.RequirementId,
        s.Execution.ExecutionRequestId, Guid.NewGuid(), Guid.NewGuid());
    private async Task<RequirementProcessingJob> Job(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == id, Token);
    }
    private async Task<string> Snapshot(bool jobs = true)
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new {
            business = await H.Snapshot(jobs),
            files = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token),
            extraction = await db.RequirementExtractionResults.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            proposals = await db.RequirementTechnicalProposals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            receipts = jobs ? await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token) : [] });
    }

    [Fact]
    public async Task Claim_PreservesExistingDocumentEvidenceExtractionAndProposal()
    {
        var s = await H.Create(running: false, verifiedPublication: true);
        var control = await OtherJob(s);
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(x => x.Id == control.Execution.JobId, Token);
            var requirement = await db.Requirements.SingleAsync(x => x.Id == job.RequirementId, Token);
            var at = job.CreatedAtUtc;
            const string hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
            var file = RequirementFile.CreateWithStoredEvidence(requirement.Id, "control.pdf", "application/pdf", 3, "tests/not-opened", at, hash, 3, at, 1);
            job.AssignExecution("structural-control", at, at.AddMinutes(10));
            var attempt = RequirementProcessingAttempt.Create(requirement.Id, s.Actor, job.CorrelationId, at);
            attempt.Start(at); job.LinkStartedAttempt(attempt.Id); requirement.StartProcessing(at);
            attempt.Complete(DocumentProcessingOutcome.Completed, at); requirement.MarkProcessed(at);
            job.SucceedWithPublicationReceipt(RequirementProcessingJob.PublicationV2, new string('A', 64), at);
            var extraction = RequirementExtractionResult.Create(attempt.Id, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, at);
            var proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, attempt.Id, false, at);
            var id = Guid.NewGuid();
            var evidence = RequirementExecutionEvidence.Create(id, requirement.Id, job.Id, job.ExecutionRequestId, attempt.Id,
                1, s.Actor, file.Id, 1, at, at, 1, new[] { RequirementExecutionDocument.Create(id, file.Id, 0,
                    file.OriginalFileName, file.ContentType, file.StorageKey, at, 3, hash, 3, at, 1, hash, 3) });
            db.AddRange(file, attempt, extraction, proposal, evidence); await db.SaveChangesAsync(Token);
        }
        var before = await Snapshot(false); var oldJob = JsonSerializer.Serialize(await Job(control.Execution.JobId));
        Assert.Equal(LeaseOperationState.Claimed, (await Service().ClaimInitialV2Async(Request(s), s.Actor, Token)).State);
        Assert.Equal(before, await Snapshot(false)); Assert.Equal(oldJob, JsonSerializer.Serialize(await Job(control.Execution.JobId)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Claim_AtomicDatabaseClock_OnlyJobAndReceipt_ExactReplay(bool publicContext)
    {
        var s = await H.Create(running: false, publicContext: publicContext, verifiedPublication: true);
        var request = Request(s); var before = await Snapshot(false); var old = await Job(request.JobId);
        var probe = new Commands(); var lower = await H.Now();
        var result = await Service(probe).ClaimInitialV2Async(request, s.Actor, Token);
        var upper = await H.Now(); var job = await Job(request.JobId);
        Assert.Equal(LeaseOperationState.Claimed, result.State);
        var receipt = Assert.IsType<JobLeaseOperation>(result.Receipt);
        Assert.Equal(request.OperationId, receipt.OperationId); Assert.Equal(receipt.OperationId, receipt.ClaimOperationId);
        Assert.Equal(request.JobId, receipt.JobId); Assert.Equal(request.RequirementId, receipt.RequirementId);
        Assert.Equal(request.ExecutionRequestId, receipt.ExecutionRequestId); Assert.Equal(s.Actor, receipt.ActorUserId);
        Assert.Equal($"exec:{request.InstanceId:D}:{request.OperationId:D}", receipt.LeaseOwner);
        Assert.Equal(receipt.AppliedAtUtc, job.StartedAtUtc); Assert.InRange(receipt.AppliedAtUtc, lower, upper);
        Assert.Equal(receipt.AppliedAtUtc, receipt.AssignedAtUtc); Assert.Equal(0, receipt.AppliedAtUtc.Ticks % 10);
        Assert.Equal(receipt.AppliedAtUtc.AddSeconds(120), receipt.ExpiresAtUtc);
        Assert.Equal(receipt.ExpiresAtUtc, job.LeaseExpiresAtUtc); Assert.Equal(receipt.LeaseOwner, job.LeaseOwner);
        Assert.Equal(1, job.LeaseGeneration); Assert.Equal((short)1, receipt.PolicyVersion);
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.ProcessingAttemptId); Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.FinishedAtUtc);
        Assert.Equal(old.CreatedAtUtc, job.CreatedAtUtc); Assert.Equal(old.CorrelationId, job.CorrelationId);
        Assert.Equal(RequirementProcessingJob.PublicationV2, job.RequiredPublicationVersion);
        Assert.Null(job.PublicationContractVersion); Assert.Null(job.PublicationContentHash);
        Assert.Equal(before, await Snapshot(false)); Assert.Equal(1, probe.Writes);
        var state = await Snapshot(); var replayProbe = new Commands();
        var replay = await Service(replayProbe).ClaimInitialV2Async(request, s.Actor, Token);
        Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State);
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(replay.Receipt));
        Assert.Equal(LeaseAvailability.Live, replay.Observation!.Availability);
        Assert.Equal(0, replayProbe.Writes); Assert.Equal(state, await Snapshot());
        await using var read = fixture.CreateDbContext();
        Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.Single(await read.JobLeaseOperations.ToListAsync(Token));
        Assert.False(read.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("actor")] [InlineData("job")] [InlineData("requirement")]
    [InlineData("request")] [InlineData("operation")] [InlineData("instance")]
    public async Task InvalidIdentifiers_DoNotOpenDatabase(string field)
    {
        var e = new ClaimInitialLease(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var actor = Guid.NewGuid();
        e = field switch { "job" => e with { JobId = Guid.Empty }, "requirement" => e with { RequirementId = Guid.Empty },
            "request" => e with { ExecutionRequestId = Guid.Empty }, "operation" => e with { OperationId = Guid.Empty },
            "instance" => e with { InstanceId = Guid.Empty }, _ => e };
        if (field == "actor") actor = Guid.Empty;
        var opened = false;
        var result = await Service(new OpenHook(() => opened = true)).ClaimInitialV2Async(e, actor, Token);
        Assert.Equal(LeaseOperationFailure.InvalidRequest, result.Failure); Assert.False(opened);
    }

    [Theory]
    [InlineData("other")] [InlineData("admin")] [InlineData("inactive-user")]
    [InlineData("inactive-client")] [InlineData("inactive-project")]
    [InlineData("request")] [InlineData("requirement")] [InlineData("public-links")]
    [InlineData("v1")] [InlineData("not-current")] [InlineData("processing")]
    [InlineData("cancelled")] [InlineData("running")] [InlineData("running-cancel")]
    [InlineData("expired")] [InlineData("active-attempt")] [InlineData("future-created")]
    public async Task Rejections_NoEffects_NoReceiptDisclosure(string scenario)
    {
        var s = await H.Create(running: scenario is "running" or "running-cancel" or "expired",
            request: scenario == "running-cancel", publicContext: scenario == "public-links", verifiedPublication: scenario != "v1");
        var e = Request(s); var actor = s.Actor; var now = await H.Now();
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario is "other" or "admin")
            {
                var other = User.CreateFromGoogle("other@test.local", "Other", null, null, now);
                if (scenario == "admin") other.ChangeRole(UserRole.Admin, now);
                db.Add(other); actor = other.Id;
            }
            if (scenario == "inactive-user") (await db.Users.SingleAsync(Token)).Deactivate(now);
            if (scenario == "inactive-client") (await db.Clients.SingleAsync(Token)).SetActive(false, s.Actor, now);
            if (scenario == "inactive-project") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Actor, now);
            if (scenario == "not-current") (await db.Requirements.SingleAsync(Token)).Cancel(now);
            if (scenario == "processing") (await db.Requirements.SingleAsync(Token)).StartProcessing(now);
            if (scenario == "active-attempt") db.Add(RequirementProcessingAttempt.Create(e.RequirementId, s.Actor, Guid.NewGuid(), now));
            await db.SaveChangesAsync(Token);
            if (scenario == "public-links") await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submissions SET requirement_id=NULL", Token);
            if (scenario == "future-created") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET created_at_utc={now.AddHours(1)} WHERE id={e.JobId}", Token);
            if (scenario == "expired") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={now.AddSeconds(-1)} WHERE id={e.JobId}", Token);
        }
        if (scenario == "cancelled") await H.Call("request", s);
        if (scenario == "request") e = e with { ExecutionRequestId = Guid.NewGuid() };
        if (scenario == "requirement") e = e with { RequirementId = Guid.NewGuid() };
        var before = await Snapshot(); var probe = new Commands();
        var result = await Service(probe).ClaimInitialV2Async(e, actor, Token);
        Assert.Contains(result.State, new[] { LeaseOperationState.Rejected, LeaseOperationState.Conflict });
        Assert.Null(result.Receipt); Assert.Null(result.Observation); Assert.Equal(before, await Snapshot());
        Assert.Equal(scenario == "future-created" ? 1 : 0, probe.Writes);
        if (scenario is "other" or "admin") Assert.Equal(LeaseOperationFailure.NotFound, result.Failure);
    }

    [Theory]
    [InlineData("instance")] [InlineData("other")] [InlineData("admin")]
    [InlineData("expired")] [InlineData("terminal")] [InlineData("context")]
    [InlineData("successor")] [InlineData("renewed")] [InlineData("started")]
    public async Task HistoricalReplay_SeparatesReceiptAndObservation_WithoutWrites(string scenario)
    {
        var s = await H.Create(running: false, verifiedPublication: true); var e = Request(s);
        var original = await Service().ClaimInitialV2Async(e, s.Actor, Token);
        Assert.Equal(LeaseOperationState.Claimed, original.State);
        var actor = s.Actor; var now = await H.Now();
        if (scenario is "terminal" or "successor" or "started")
        {
            var execution = s.Execution with { LeaseOwner = original.Receipt!.LeaseOwner };
            if (scenario is "successor" or "started")
            {
                var started = await H.StartService().StartVerifiedAsync(execution, s.Actor, Token);
                Assert.Equal(AssignedJobStartState.Started, started.State);
                execution = execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
            }
            if (scenario != "started")
                Assert.Equal(JobTerminationState.Failed, (await H.Call("fail", s with { Execution = execution })).State);
            now = await H.Now();
        }
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario is "other" or "admin")
            {
                var other = User.CreateFromGoogle("replay-other@test.local", "Other", null, null, now);
                if (scenario == "admin") other.ChangeRole(UserRole.Admin, now);
                db.Add(other); actor = other.Id;
            }
            if (scenario == "context") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Actor, now);
            if (scenario == "successor")
            {
                var old = await db.Requirements.SingleAsync(Token);
                var next = Requirement.Create(old.PreQuoteId, s.Actor, RequirementCommercialLine.Essential, now);
                old.SupersedeBy(next.Id, now); next.MarkAsReplacementOf(old.Id); db.Add(next);
            }
            await db.SaveChangesAsync(Token);
            // Controlled historical clock fixture; no claim/renew operation or expired lease revival.
            if (scenario == "expired")
            {
                await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET started_at_utc=started_at_utc-interval '5 minutes',lease_expires_at_utc=lease_expires_at_utc-interval '5 minutes'", Token);
                await db.Database.ExecuteSqlRawAsync("UPDATE core.job_lease_operations SET assigned_at_utc=assigned_at_utc-interval '5 minutes',applied_at_utc=applied_at_utc-interval '5 minutes',expires_at_utc=expires_at_utc-interval '5 minutes'", Token);
                original = original with { Receipt = await db.JobLeaseOperations.AsNoTracking().SingleAsync(Token) };
            }
            if (scenario == "renewed") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=lease_expires_at_utc+interval '1 minute'", Token);
        }
        if (scenario == "instance") e = e with { InstanceId = Guid.NewGuid() };
        var before = await Snapshot(); var probe = new Commands();
        var result = await Service(probe).ClaimInitialV2Async(e, actor, Token);
        if (scenario is "other" or "admin" or "instance")
        {
            Assert.Equal(scenario == "instance" ? LeaseOperationState.Conflict : LeaseOperationState.Rejected, result.State);
            Assert.Null(result.Receipt); Assert.Null(result.Observation);
        }
        else
        {
            Assert.Equal(LeaseOperationState.AlreadyApplied, result.State);
            Assert.Equal(JsonSerializer.Serialize(original.Receipt), JsonSerializer.Serialize(result.Receipt));
            var availability = scenario switch { "expired" => LeaseAvailability.Expired, "terminal" or "successor" => LeaseAvailability.Terminal,
                "context" => LeaseAvailability.ContextChanged, "started" => LeaseAvailability.Live, _ => LeaseAvailability.Superseded };
            Assert.Equal(availability, result.Observation!.Availability);
        }
        Assert.Equal(0, probe.Writes); Assert.Equal(before, await Snapshot());
    }

    private sealed class Commands : DbCommandInterceptor
    {
        internal int Writes;
        internal Func<DbCommand, CancellationToken, Task>? Before;
        internal Func<DbCommand, DbDataReader, CancellationToken, Task>? After;
        private void Count(DbCommand command)
        {
            var sql = command.CommandText.TrimStart();
            if (sql.StartsWith("WITH clock") || sql.StartsWith("UPDATE ") || sql.StartsWith("INSERT ") || sql.StartsWith("DELETE ")) Writes++;
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count(command); if (Before is not null) await Before(command, cancellationToken); return result; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        { if (After is not null) await After(command, result, cancellationToken); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Count(command); return ValueTask.FromResult(result); }
    }
}
