using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.PreQuotes.ProcessRequirement;
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
    private sealed record ClaimedSeed(Seed Seed, ClaimInitialLease Request, JobLeaseOperation Receipt);
    private ClaimInitialLeaseService CombinedClaim(params IInterceptor[] hooks) => new(new InitialLeaseClaimStore(H.Options(hooks)));
    private RenewActiveLeaseService CombinedRenew(params IInterceptor[] hooks) => new(new ActiveLeaseRenewalStore(H.Options(hooks)));

    private async Task<ClaimedSeed> CombinedSetup(Files files, bool publicContext = false)
    {
        var seed = await SeedFiles(files, publicContext, start: false, assigned: false);
        var e = seed.Execution;
        var request = new ClaimInitialLease(e.JobId, e.RequirementId, e.ExecutionRequestId, Guid.NewGuid(), Guid.NewGuid());
        var result = await CombinedClaim().ClaimInitialV2Async(request, seed.Actor, Token);
        Assert.Equal(LeaseOperationState.Claimed, result.State);
        var receipt = Assert.IsType<JobLeaseOperation>(result.Receipt);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Equal(0, job.AttemptCount); Assert.Null(job.ProcessingAttemptId);
        Assert.Equal(RequirementStatus.Pending, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.Empty(await db.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.Empty(await db.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        Assert.Single(await db.JobLeaseOperations.ToListAsync(Token));
        Assert.Equal(receipt.AppliedAtUtc, job.StartedAtUtc); Assert.Equal(receipt.ExpiresAtUtc, job.LeaseExpiresAtUtc);
        Assert.Equal(receipt.LeaseOwner, job.LeaseOwner); Assert.Equal(receipt.LeaseGeneration, job.LeaseGeneration);
        return new(seed with { Execution = new(receipt.JobId, receipt.ExecutionRequestId, receipt.RequirementId,
            receipt.LeaseOwner, receipt.LeaseGeneration) }, request, receipt);
    }

    private async Task<RequirementProcessingJob> CombinedJob()
    {
        await using var db = fixture.CreateDbContext();
        return await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
    }

    private async Task<RenewActiveLease> CombinedRequest(ClaimedSeed s)
    {
        var job = await CombinedJob();
        Assert.Equal(1, job.AttemptCount); Assert.NotNull(job.ProcessingAttemptId);
        return new(job.Id, job.RequirementId, job.ExecutionRequestId, Guid.NewGuid(), s.Receipt.OperationId,
            s.Receipt.LeaseOwner, s.Receipt.LeaseGeneration, job.LeaseExpiresAtUtc!.Value, job.ProcessingAttemptId);
    }

    private async Task<string> CombinedSnapshot(bool mutableLease = true)
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new {
            business = await H.Snapshot(mutableLease),
            files = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token),
            extraction = await db.RequirementExtractionResults.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            proposals = await db.RequirementTechnicalProposals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            receipts = mutableLease ? await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token) : [] });
    }

    private static string CombinedExceptExpiry(RequirementProcessingJob job)
    {
        var node = JsonSerializer.SerializeToNode(job)!.AsObject(); node.Remove(nameof(job.LeaseExpiresAtUtc)); return node.ToJsonString();
    }

    private async Task CombinedUnlocked(ClaimedSeed s)
    {
        await using var db = fixture.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.pre_quotes WHERE id={s.Seed.Quote} FOR UPDATE NOWAIT", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirements WHERE id={s.Request.RequirementId} FOR UPDATE NOWAIT", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM core.requirement_processing_jobs WHERE id={s.Request.JobId} FOR UPDATE NOWAIT", Token);
        output.WriteLine($"Combined I/O paused: independent NOWAIT PID {((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID}.");
    }

    private async Task CombinedReceiptUnchanged(JobLeaseOperation expected)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(await db.JobLeaseOperations.AsNoTracking()
            .SingleAsync(x => x.OperationId == expected.OperationId, Token)));
    }

    private async Task CombinedPublished(ClaimedSeed s, AssignedExecutionResult result, Files files, Copies copies, ConsumingClient client, int renewals)
    {
        Assert.Equal(AssignedExecutionOutcome.Published, result.Outcome); Assert.Equal(AssignedExecutionStage.Publication, result.Stage);
        Assert.Equal(JobPublicationState.Published, result.Publication!.State); Assert.Null(result.Termination);
        var receipt = result.Publication.Receipt!;
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        var attempt = await db.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
        var evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).SingleAsync(Token);
        var extraction = await db.RequirementExtractionResults.AsNoTracking().SingleAsync(Token);
        var proposal = await db.RequirementTechnicalProposals.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Succeeded, job.State); Assert.Equal(1, job.AttemptCount);
        Assert.Null(job.LeaseOwner); Assert.Null(job.LeaseExpiresAtUtc); Assert.Null(job.LastErrorCode);
        Assert.Equal(s.Receipt.AssignedAtUtc, job.StartedAtUtc); Assert.Equal(s.Receipt.LeaseGeneration, job.LeaseGeneration);
        Assert.Equal(s.Request.JobId, job.Id); Assert.Equal(s.Request.ExecutionRequestId, job.ExecutionRequestId);
        Assert.Equal(job.Id, evidence.JobId); Assert.Equal(job.RequirementId, evidence.RequirementId);
        Assert.Equal(job.ExecutionRequestId, evidence.ExecutionRequestId); Assert.Equal(job.LeaseGeneration, evidence.LeaseGeneration);
        Assert.Equal(s.Seed.Actor, evidence.ActorUserId); Assert.Equal(attempt.Id, evidence.ProcessingAttemptId);
        Assert.Equal(attempt.Id, job.ProcessingAttemptId); Assert.Equal(job.CorrelationId, attempt.CorrelationId);
        // The shared minimal extraction has no element category; existing mapping requires review.
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState); Assert.Equal(DocumentProcessingOutcome.RequiresReview, attempt.Outcome);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, receipt.Outcome);
        Assert.Equal(RequirementTechnicalProposalStatus.RequiresReview, proposal.Status);
        Assert.Equal(job.Id, receipt.JobId); Assert.Equal(job.RequirementId, receipt.RequirementId);
        Assert.Equal(job.ExecutionRequestId, receipt.ExecutionRequestId); Assert.Equal(attempt.Id, receipt.ProcessingAttemptId);
        Assert.Equal(job.CorrelationId, receipt.CorrelationId); Assert.Equal(extraction.Id, receipt.ExtractionId); Assert.Equal(proposal.Id, receipt.ProposalId);
        Assert.Equal(PreparedJobPublication.Version2, job.RequiredPublicationVersion); Assert.Equal(PreparedJobPublication.Version2, receipt.ContractVersion);
        Assert.Equal(receipt.ContractVersion, job.PublicationContractVersion); Assert.Equal(receipt.ContentHash, job.PublicationContentHash);
        Assert.Equal(receipt.FinishedAtUtc, job.FinishedAtUtc); Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
        Assert.Null(proposal.CommercialConfirmedAtUtc);
        Assert.Equal(RequirementStatus.Processed, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.Equal(job.FinishedAtUtc, (await db.PreQuotes.SingleAsync(Token)).UpdatedAtUtc);
        Assert.Equal(1, await db.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Claim, Token));
        Assert.Equal(renewals, await db.JobLeaseOperations.CountAsync(x => x.Kind == JobLeaseOperationKind.Renew, Token));
        Assert.Equal(2, evidence.Files.Count); Assert.Equal(2, files.Opens); Assert.Equal(2, copies.Created.Count); Assert.Equal(1, client.Calls);
        Assert.Equal(attempt.Id, client.Request!.ProcessingAttemptId); Assert.Equal(evidence.PrimaryDocumentId, client.Request.DocumentId);
        Assert.All(client.Bytes, b => Assert.Equal("abc"u8.ToArray(), b));
        Released(files, copies, client); await CombinedReceiptUnchanged(s.Receipt);
    }

    [Theory]
    [InlineData(false, "copy")] [InlineData(true, "copy")]
    [InlineData(false, "provider")] [InlineData(true, "provider")]
    public async Task Combined_ClaimCoordinatorRenew_PublicManual_PublishAndReplay(bool publicContext, string phase)
    {
        await using var files = new Files(); var s = await CombinedSetup(files, publicContext);
        var copies = new Copies(); var client = new ConsumingClient(); var reached = Signal(); var release = Signal(); var pause = 0;
        async Task Pause() { if (Interlocked.Increment(ref pause) == 1) { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); } }
        if (phase == "copy") copies.AfterCopy = Pause; else client.DuringRead = Pause;
        var running = Coordinator(files, copies, client).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token);
        JobLeaseOperation? renewed = null; RenewActiveLease? request = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.False(running.IsCompleted); Assert.Equal(phase == "copy" ? 0 : 1, client.Calls);
            Assert.All(copies.Created, c => Assert.False(c.Disposed)); await CombinedUnlocked(s);
            request = await CombinedRequest(s);
            var before = await CombinedSnapshot(false); var job = CombinedExceptExpiry(await CombinedJob()); var writes = new CombinedCommands();
            var renewal = await CombinedRenew(writes).RenewActiveV2Async(request, s.Seed.Actor, Token).WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.Equal(LeaseOperationState.Renewed, renewal.State); renewed = renewal.Receipt!; Assert.True(renewed.IsEffectiveExtension);
            Assert.Equal(request.ExpectedProcessingAttemptId, renewed.ExpectedProcessingAttemptId); Assert.Equal(1, writes.Writes);
            Assert.Equal(before, await CombinedSnapshot(false)); Assert.Equal(job, CombinedExceptExpiry(await CombinedJob()));
            Assert.Equal(renewed.ExpiresAtUtc, (await CombinedJob()).LeaseExpiresAtUtc); await CombinedReceiptUnchanged(s.Receipt);
            var all = await CombinedSnapshot(); var replayWrites = new CombinedCommands();
            Assert.Equal(LeaseOperationState.AlreadyApplied, (await CombinedClaim(replayWrites).ClaimInitialV2Async(s.Request, s.Seed.Actor, Token)).State);
            var replay = await CombinedRenew(replayWrites).RenewActiveV2Async(request, s.Seed.Actor, Token);
            Assert.Equal(LeaseOperationState.AlreadyApplied, replay.State); Assert.Equal(JsonSerializer.Serialize(renewed), JsonSerializer.Serialize(replay.Receipt));
            var unused = new Copies(); var losing = await Coordinator(files, unused, client, start: [replayWrites]).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token);
            Assert.Equal(AssignedExecutionOutcome.AlreadyStarted, losing.Outcome); Assert.Equal(AssignedExecutionStage.Start, losing.Stage);
            Assert.Empty(unused.Created); Assert.Equal(0, replayWrites.Writes); Assert.Equal(all, await CombinedSnapshot());
            Assert.Equal(phase == "copy" ? 0 : 1, client.Calls); Assert.All(copies.Created, c => Assert.False(c.Disposed));
            output.WriteLine($"Combined {publicContext}/{phase}: Renew PID {writes.Pid}, effective extension, expected attempt {request.ExpectedProcessingAttemptId}, replay DML=0; winner resources live.");
        }
        finally { release.TrySetResult(); await running; }
        await CombinedPublished(s, await running, files, copies, client, 1);
        await CombinedReceiptUnchanged(renewed!);
        var terminal = await CombinedSnapshot(); var terminalWrites = new CombinedCommands();
        var historical = await CombinedRenew(terminalWrites).RenewActiveV2Async(request!, s.Seed.Actor, Token);
        Assert.Equal(LeaseOperationState.AlreadyApplied, historical.State); Assert.Equal(LeaseAvailability.Terminal, historical.Observation!.Availability);
        Assert.Equal(LeaseOperationState.AlreadyApplied, (await CombinedClaim(terminalWrites).ClaimInitialV2Async(s.Request, s.Seed.Actor, Token)).State);
        Assert.Equal(AssignedExecutionOutcome.AlreadyStarted, (await Coordinator(files, new Copies(), client, start: [terminalWrites]).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token)).Outcome);
        Assert.Equal(0, terminalWrites.Writes); Assert.Equal(terminal, await CombinedSnapshot()); Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Combined_ClaimCoordinator_CancellationOrExpiry_NoPublication(bool expired)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var copies = new Copies(); var reached = Signal(); var release = Signal();
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); } };
        var closes = new CombinedCommands(); var running = Coordinator(files, copies, client, termination: [closes]).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token);
        string? beforeLate = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await CombinedUnlocked(s);
            if (expired)
            {
                // Only current expiry changes; Claim/start/attempt/evidence stay intact. Not a prior Renew.
                var horizon = (await H.Now()).AddSeconds(1);
                await using var db = fixture.CreateDbContext();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={horizon} WHERE id={s.Request.JobId}", Token);
                Assert.True(await H.Now() < horizon);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                DateTimeOffset observed;
                while ((observed = await H.Now()) <= horizon) await Task.Delay(20, deadline.Token);
                output.WriteLine($"Combined expiry observed by DB: {observed:O} > {horizon:O}; no productive duration change.");
            }
            else Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(s.Seed))).State);
            var request = await CombinedRequest(s); beforeLate = await CombinedSnapshot(); var writes = new CombinedCommands();
            var denied = await CombinedRenew(writes).RenewActiveV2Async(request, s.Seed.Actor, Token);
            Assert.Equal(LeaseOperationState.Rejected, denied.State);
            Assert.Equal(expired ? LeaseOperationFailure.Expired : LeaseOperationFailure.CancellationRequested, denied.Failure);
            Assert.Null(denied.Receipt); Assert.Null(denied.Observation); Assert.Equal(expired ? 1 : 0, writes.Writes);
            Assert.Equal(beforeLate, await CombinedSnapshot()); Assert.Equal(1, client.Calls); Assert.False(running.IsCompleted);
        }
        finally { release.TrySetResult(); await running; }
        var result = await running;
        await using var read = fixture.CreateDbContext();
        var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        var attempt = await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
        var req = await read.Requirements.AsNoTracking().SingleAsync(Token);
        Assert.Equal(1, job.AttemptCount); Assert.Equal(attempt.Id, job.ProcessingAttemptId);
        Assert.Null(job.PublicationContractVersion); Assert.Null(job.PublicationContentHash);
        Assert.Empty(await read.RequirementTechnicalProposals.ToListAsync(Token)); Assert.Empty(await read.RequirementExtractionResults.ToListAsync(Token));
        Assert.Single(await read.JobLeaseOperations.ToListAsync(Token)); Assert.Single(await read.Set<RequirementExecutionEvidence>().ToListAsync(Token));
        if (expired)
        {
            Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome); Assert.Equal(AssignedExecutionStage.Publication, result.Stage);
            Assert.Equal(JobPublicationState.Conflict, result.Publication!.State); Assert.Equal(JobPublicationFailure.LeaseConflict, result.Publication.Failure);
            Assert.Null(result.Termination); Assert.Equal(0, closes.Writes); Assert.Equal(beforeLate, await CombinedSnapshot());
            Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Equal(RequirementStatus.Processing, req.Status);
            Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState); Assert.Null(job.FinishedAtUtc); Assert.Null(job.LastErrorCode);
            Assert.Null(job.CancellationRequestedAtUtc); Assert.Equal(s.Receipt.LeaseOwner, job.LeaseOwner);
        }
        else
        {
            Assert.Equal(AssignedExecutionOutcome.Cancelled, result.Outcome); Assert.Equal(AssignedExecutionStage.Termination, result.Stage);
            Assert.Equal(JobPublicationFailure.CancellationRequested, result.Publication!.Failure);
            Assert.Equal(JobTerminationState.CancellationConfirmed, result.Termination!.State); Assert.True(closes.Writes > 0);
            Assert.Equal(RequirementProcessingJobState.Cancelled, job.State); Assert.Equal(RequirementStatus.Pending, req.Status);
            Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState); Assert.Equal(DocumentProcessingOutcome.Cancelled, attempt.Outcome);
            Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc); Assert.Equal(job.FinishedAtUtc, req.UpdatedAtUtc);
            Assert.Equal(job.FinishedAtUtc, (await read.PreQuotes.SingleAsync(Token)).UpdatedAtUtc);
            Assert.NotNull(job.CancellationRequestedAtUtc); Assert.Null(job.LeaseOwner); Assert.Null(job.LeaseExpiresAtUtc);
            var terminal = await CombinedSnapshot(); var replay = new CombinedCommands();
            Assert.Equal(JobTerminationState.AlreadyRecognized, (await H.Call("confirm", await WithAttempt(s.Seed), H.Service(replay))).State);
            Assert.Equal(0, replay.Writes); Assert.Equal(terminal, await CombinedSnapshot());
        }
        Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens); Assert.Equal(2, copies.Created.Count);
        Released(files, copies, client); await CombinedReceiptUnchanged(s.Receipt);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Combined_RenewCommitLoss_DoesNotControlInFlightExecution(bool unavailable)
    {
        await using var files = new Files(); var s = await CombinedSetup(files);
        var copies = new Copies(); var reached = Signal(); var release = Signal();
        var client = new ConsumingClient { DuringRead = async () => { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); } };
        var running = Coordinator(files, copies, client).ExecuteAsync(s.Seed.Execution, s.Seed.Actor, Token);
        JobLeaseOperation? persisted = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); var request = await CombinedRequest(s);
            var before = await CombinedSnapshot(false); var committed = false; var writes = new CombinedCommands();
            var loss = new JobProtocolTestSupport.AfterCommit(() => { committed = true; throw new IOException("renew commit response loss"); });
            var observer = new OpenHook(() => { if (committed && unavailable) throw new IOException("renew observer unavailable"); });
            var result = await CombinedRenew(writes, loss, observer).RenewActiveV2Async(request, s.Seed.Actor, Token);
            Assert.True(committed); Assert.Equal(unavailable ? LeaseOperationState.Indeterminate : LeaseOperationState.AlreadyApplied, result.State);
            if (unavailable) { Assert.Null(result.Receipt); Assert.Null(result.Observation); }
            Assert.Equal(1, writes.Writes); Assert.Equal(before, await CombinedSnapshot(false));
            Assert.False(running.IsCompleted); Assert.Equal(1, client.Calls); Assert.All(copies.Created, c => Assert.False(c.Disposed));
            await using var db = fixture.CreateDbContext();
            persisted = await db.JobLeaseOperations.AsNoTracking().SingleAsync(x => x.OperationId == request.OperationId, Token);
            Assert.Equal(2, await db.JobLeaseOperations.CountAsync(Token)); Assert.Equal(RequirementProcessingJobState.Running, (await CombinedJob()).State);
            Assert.Equal(persisted.ExpiresAtUtc, (await CombinedJob()).LeaseExpiresAtUtc); Assert.True(persisted.IsEffectiveExtension);
            if (!unavailable) Assert.Equal(JsonSerializer.Serialize(persisted), JsonSerializer.Serialize(result.Receipt));
            output.WriteLine($"Combined Renew {result.State}: real commit, one mutation, same OperationId, execution still paused/Running; no supervisor decision implemented.");
        }
        finally
        {
            // Release is unconditional test cleanup, not a policy branch on Renew's outcome.
            release.TrySetResult(); await running;
        }
        await CombinedPublished(s, await running, files, copies, client, 1); await CombinedReceiptUnchanged(persisted!);
    }

    private sealed class CombinedCommands : DbCommandInterceptor
    {
        internal int Writes; internal int Pid;
        private void Observe(DbCommand command)
        {
            Pid = ((NpgsqlConnection)command.Connection!).ProcessID;
            var text = command.CommandText.TrimStart();
            if (new[] { "WITH clock", "INSERT ", "UPDATE ", "DELETE " }.Any(x => text.StartsWith(x, StringComparison.OrdinalIgnoreCase))) Writes++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
    }
}
