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
    private sealed record DrainedClosure(Seed Seed, LocalDrainCompletion Drain, LiveCancellation Input);
    private ObserveForClosureService ClosureObserver(params IInterceptor[] hooks) => new(new ExecutionClosureObservationStore(H.Options(hooks)));
    private ConfirmStoppedCancellationService Closer(IInterceptor[]? read = null, IInterceptor[]? write = null) =>
        new(ClosureObserver(read ?? []), H.Service(write ?? []));

    private async Task<DrainedClosure> ClosureSetup(Files files, bool attempt, bool request = true)
    {
        var seed = await SeedFiles(files, start: false, assigned: false);
        var input = SupervisionRequest(seed); var copies = new Copies(); var client = new ConsumingClient();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (attempt) client.Action = (r, _) => { caller.Cancel(); return Task.FromResult(ConsumingClient.Response(r)); };
        var calls = SupervisionProbe();
        var invocation = new LocalExecutionInvocation(calls,
            Coordinator(files, copies, client, start: attempt ? null : [new OpenHook(caller.Cancel)]), new(calls), new(calls), TimeProvider.System);
        Assert.Null(invocation.DrainCompletion);
        Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => invocation.ExecuteAsync(input, seed.Actor, caller.Token))).CancellationToken);
        var proof = Assert.IsType<LocalDrainCompletion>(invocation.DrainCompletion);
        Assert.Equal(1, calls.Claims); Assert.Equal(0, calls.Renewals); Assert.Equal(attempt ? 1 : 0, client.Calls);
        Released(files, copies, client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.ExecuteAsync(input, seed.Actor, Token));
        var job = await CombinedJob(); Assert.Equal(attempt ? 1 : 0, job.AttemptCount);
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.CancellationRequestedAtUtc);
        seed = seed with { Execution = new(job.Id, job.ExecutionRequestId, job.RequirementId, job.LeaseOwner!, job.LeaseGeneration, job.ProcessingAttemptId) };
        if (request) Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", seed)).State);
        job = await CombinedJob();
        return new(seed, proof, new(proof.Identity, proof.LeaseOwner, proof.LeaseGeneration, proof.AssignedAtUtc,
            job.ProcessingAttemptId, job.CancellationRequestedAtUtc ?? await H.Now()));
    }

    [Theory]
    [InlineData(false, "active")] [InlineData(true, "active")]
    [InlineData(false, "project")] [InlineData(true, "project")]
    [InlineData(false, "client")] [InlineData(true, "client")]
    public async Task ClosureA_Confirm_ExactEffects_Replay_NoDrainForHistory(bool attempt, string context)
    {
        await using var files = new Files(); var s = await ClosureSetup(files, attempt);
        await using (var db = fixture.CreateDbContext())
        {
            if (context == "project") (await db.Projects.SingleAsync(Token)).SetActive(false, s.Seed.Actor, await H.Now());
            // Context fixture only; all constraints remain active.
            if (context == "client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
            await db.SaveChangesAsync(Token);
        }
        await using var before = fixture.CreateDbContext();
        var reqBefore = await before.Requirements.AsNoTracking().SingleAsync(Token);
        var quoteBefore = await before.PreQuotes.AsNoTracking().SingleAsync(Token);
        var immutable = await ClosureImmutable(); var jobBefore = await CombinedJob();
        var result = await Closer().ConfirmStoppedCancellationAsync(s.Input, s.Drain, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.State); Assert.Equal(JobTerminationFailure.None, result.Failure);
        await using var dbRead = fixture.CreateDbContext();
        var job = await CombinedJob(); var req = await dbRead.Requirements.AsNoTracking().SingleAsync(Token);
        var quote = await dbRead.PreQuotes.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Cancelled, job.State); Assert.Equal(RequirementProcessingJob.PublicationV2, job.RequiredPublicationVersion);
        Assert.Null(job.PublicationContractVersion); Assert.Null(job.PublicationContentHash); Assert.Null(job.LeaseOwner); Assert.Null(job.LeaseExpiresAtUtc);
        Assert.Null(job.LastErrorCode); Assert.Equal(jobBefore.StartedAtUtc, job.StartedAtUtc); Assert.Equal(jobBefore.CorrelationId, job.CorrelationId);
        Assert.Equal(jobBefore.ExecutionRequestId, job.ExecutionRequestId); Assert.Equal(jobBefore.LeaseGeneration, job.LeaseGeneration);
        Assert.Equal(jobBefore.ProcessingAttemptId, job.ProcessingAttemptId); Assert.Equal(jobBefore.AttemptCount, job.AttemptCount);
        Assert.Equal(jobBefore.CancellationRequestedAtUtc, job.CancellationRequestedAtUtc); Assert.Equal(job.FinishedAtUtc, result.Receipt!.FinishedAtUtc);
        Assert.Equal(RequirementStatus.Pending, req.Status); Assert.Equal(reqBefore.IsActive, req.IsActive); Assert.True(req.IsCurrent);
        Assert.Equal(attempt ? job.FinishedAtUtc : reqBefore.UpdatedAtUtc, req.UpdatedAtUtc);
        Assert.Equal(attempt ? job.FinishedAtUtc : quoteBefore.UpdatedAtUtc, quote.UpdatedAtUtc);
        var attempts = await dbRead.RequirementProcessingAttempts.AsNoTracking().ToListAsync(Token);
        if (attempt)
        {
            var a = Assert.Single(attempts); Assert.Equal(job.ProcessingAttemptId, a.Id); Assert.Equal(job.CorrelationId, a.CorrelationId);
            Assert.Equal(DocumentProcessingState.Finished, a.ProcessingState); Assert.Equal(DocumentProcessingOutcome.Cancelled, a.Outcome);
            Assert.Equal(job.FinishedAtUtc, a.CompletedAtUtc); Assert.Null(a.ErrorCode);
        }
        else Assert.Empty(attempts);
        Assert.Equal(immutable, await ClosureImmutable());
        var terminal = await CombinedSnapshot(); var writes = new CombinedCommands();
        var replay = await Closer([writes], [writes]).ConfirmStoppedCancellationAsync(s.Input, null, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.AlreadyRecognized, replay.State); Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(terminal, await CombinedSnapshot());
        output.WriteLine($"Closure A attempt={attempt}/{context}: exact terminal effects, immutable documents/evidence/receipts, historical replay DML=0 without drain.");
    }

    private async Task<string> ClosureImmutable()
    {
        await using var db = fixture.CreateDbContext();
        return System.Text.Json.JsonSerializer.Serialize(new {
            files = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token),
            extraction = await db.RequirementExtractionResults.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            proposals = await db.RequirementTechnicalProposals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            receipts = await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token) });
    }

    [Theory]
    [InlineData("no-request", JobTerminationFailure.CancellationRequired)]
    [InlineData("no-drain", JobTerminationFailure.ContextConflict)]
    [InlineData("not-current", JobTerminationFailure.StateConflict)]
    [InlineData("state", JobTerminationFailure.StateConflict)]
    [InlineData("expected-time", JobTerminationFailure.IdentityConflict)]
    [InlineData("expected-attempt", JobTerminationFailure.IdentityConflict)]
    [InlineData("expired", JobTerminationFailure.LeaseConflict)]
    public async Task ClosureA_Confirm_PreflightRejections_NoConfirm(string mode, JobTerminationFailure failure)
    {
        await using var files = new Files(); var s = await ClosureSetup(files, false, mode != "no-request");
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "not-current") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET is_active=false", Token);
            if (mode == "state") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET status='Processing'", Token);
            if (mode == "expired")
            {
                var expiry = s.Drain.AssignedAtUtc.AddTicks(10);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry}", Token);
                Assert.True(await H.Now() > expiry);
            }
        }
        var input = mode == "expected-time" ? s.Input with { ExpectedCancellationRequestedAtUtc = s.Input.ExpectedCancellationRequestedAtUtc.AddTicks(10) }
            : mode == "expected-attempt" ? s.Input with { ExpectedProcessingAttemptId = Guid.NewGuid() } : s.Input;
        var opened = 0; var writes = new CombinedCommands(); var before = await CombinedSnapshot();
        var result = await Closer([writes], [writes, new OpenHook(() => opened++)])
            .ConfirmStoppedCancellationAsync(input, mode == "no-drain" ? null : s.Drain, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationState.Conflict, result.State); Assert.Equal(failure, result.Failure); Assert.Null(result.Receipt);
        Assert.Equal(0, opened); Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        if (mode == "expired") Assert.Equal(JobTerminationFailure.LeaseConflict, (await H.Call("confirm", s.Seed)).Failure);
    }

    [Fact]
    public async Task ClosureA_RealOwnerTransfer_ClosureOnly_NotExecutionAdoption()
    {
        await using var files = new Files(); var s = await ClosureSetup(files, true);
        Guid next;
        await using (var db = fixture.CreateDbContext())
        {
            var user = User.CreateFromGoogle("closure-owner@test.local", "Next", null, null, await H.Now()); db.Add(user);
            await db.SaveChangesAsync(Token); next = user.Id;
            // No transfer API exists. Persist the actual FK change, not a fake observer identity.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={next}", Token);
        }
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var old = await ClosureObserver(writes).ObserveForClosureAsync(s.Input.Identity, s.Seed.Actor, Token);
        Assert.Equal(JobTerminationFailure.NotFound, old.Failure); Assert.Null(old.Snapshot);
        var observed = await ClosureObserver(writes).ObserveForClosureAsync(s.Input.Identity, next, Token);
        Assert.Equal(JobTerminationFailure.None, observed.Failure); Assert.Equal(next, observed.Snapshot!.Execution.ActorUserId);
        Assert.Equal(s.Seed.Actor, observed.Snapshot.ClaimActorUserId);
        var denied = await new ObserveAssignedExecutionService(new AssignedExecutionObservationStore(H.Options(writes)))
            .ObserveAsync(new(s.Input.Identity.JobId, s.Input.Identity.RequirementId, s.Input.Identity.ExecutionRequestId, s.Input.Identity.ClaimOperationId), next, Token);
        Assert.Equal(LeaseOperationFailure.ReceiptConflict, denied.Failure); Assert.Null(denied.Snapshot);
        var job = await CombinedJob();
        var renewal = await CombinedRenew(writes).RenewActiveV2Async(new(job.Id, job.RequirementId, job.ExecutionRequestId,
            Guid.NewGuid(), s.Input.Identity.ClaimOperationId, s.Input.LeaseOwner, 1, job.LeaseExpiresAtUtc!.Value, job.ProcessingAttemptId), next, Token);
        Assert.Equal(LeaseOperationFailure.ReceiptConflict, renewal.Failure); Assert.Null(renewal.Receipt); Assert.Null(renewal.Observation);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
        var immutable = await ClosureImmutable();
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await Closer().ConfirmStoppedCancellationAsync(s.Input, s.Drain, next, Token)).State);
        Assert.Equal(immutable, await ClosureImmutable());
    }
}
