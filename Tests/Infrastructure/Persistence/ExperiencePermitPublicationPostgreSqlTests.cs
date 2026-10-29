using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private async Task<(AssignedJobExecution Execution, PreparedJobPublication Snapshot)> PermitPublication(
        Files files, Copies copies, ConsumingClient client, ExperienceExecutionPermit permit)
    {
        var a = permit.Admission;
        var claimed = await CombinedClaim().ClaimInitialV2Async(new(a.JobId, a.RequirementId, a.ExecutionRequestId,
            a.ClaimOperationId, a.InstanceId) { Permit = permit }, a.ActorUserId, Token);
        Assert.Equal(LeaseOperationState.Claimed, claimed.State);
        var e = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId, claimed.Receipt!.LeaseOwner, 1) { Permit = permit };
        var started = await H.StartService().StartVerifiedAsync(e, a.ActorUserId, Token);
        Assert.Equal(AssignedJobStartState.Started, started.State);
        e = e with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
        var store = Store(files, copies);
        var documents = await store.PrepareAsync(e, a.ActorUserId, Token);
        Assert.Equal(ExecutionDocumentPreparationState.Registered, documents.State);
        var consumed = await Consumer(files, copies, client).ConsumeAsync(documents.Package!, e, a.ActorUserId, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, consumed.State);
        var snapshot = await new PrepareJobPublicationService(new(NullLogger.Instance),
            PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()), store)
            .PrepareAsync(consumed.Result!, RequirementCommercialLine.Essential, Token);
        return (e, snapshot);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_RevokeVersusPublication_BothPostgreSqlOrders(bool revokeFirst)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files);
        var copies = new Copies(); var client = new ConsumingClient(); var a = permit.Admission;
        var (e, snapshot) = await PermitPublication(files, copies, client, permit);
        var gate = new PermitCommitGate(); var probe = new JobProtocolTestSupport.QuoteProbe();
        Task<ExperienceRevocationResult>? revoke = null; Task<JobPublicationResult>? publish = null;
        JobPublicationResult? result = null;
        try
        {
            if (revokeFirst) revoke = new ExperienceAuthorizationStore(H.Options(gate)).RevokeAsync(a.JobId, a.ActorUserId, Token);
            else publish = new JobPublicationStore(H.Options(gate)).PublishAsync(e, a.ActorUserId, snapshot, Token);
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            if (revokeFirst) publish = new JobPublicationStore(H.Options(probe)).PublishAsync(e, a.ActorUserId, snapshot, Token);
            else revoke = new ExperienceAuthorizationStore(H.Options(probe)).RevokeAsync(a.JobId, a.ActorUserId, Token);
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(winner, loser); await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Permit publication revokeFirst={revokeFirst}, winner={winner}, blocked={loser}; pg_blocking_pids observed.");
        }
        finally
        {
            gate.Release.TrySetResult();
            if (publish is not null) result = await publish;
            if (revoke is not null) Assert.Equal(ExperienceRevocationState.Revoked, (await revoke).State);
        }
        Assert.NotNull(result);
        Assert.Equal(revokeFirst ? JobPublicationState.Conflict : JobPublicationState.Published, result.State);
        Assert.Equal(revokeFirst ? JobPublicationFailure.AuthorizationConflict : JobPublicationFailure.None, result.Failure);
        var job = await CombinedJob();
        Assert.Equal(revokeFirst ? RequirementProcessingJobState.Running : RequirementProcessingJobState.Succeeded, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.NotNull(job.AuthorizationRevokedAtUtc);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(revokeFirst ? 0 : 1, await db.RequirementTechnicalProposals.CountAsync(Token));
        Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Fact]
    public async Task ExperiencePermit_PublicationOwnCommitAfterRevocation_ExactReceiptAndNoRedispatch()
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files);
        var copies = new Copies(); var client = new ConsumingClient(); var a = permit.Admission;
        var (e, snapshot) = await PermitPublication(files, copies, client, permit);
        ExperienceRevocationResult? revocation = null;
        Exception? callbackFailure = null;
        var callbackCompleted = false;
        var hook = new SupervisionAfterCommit(async () =>
        {
            try
            {
                revocation = await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(a.JobId, a.ActorUserId, Token);
                callbackCompleted = true;
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                throw;
            }
            throw new IOException("Response lost after publication and revocation.");
        });
        var result = await new JobPublicationStore(H.Options(hook)).PublishAsync(e, a.ActorUserId, snapshot, Token);
        Assert.Null(callbackFailure);
        Assert.True(callbackCompleted);
        Assert.NotNull(revocation);
        Assert.Equal(ExperienceRevocationState.Revoked, revocation.State);
        Assert.NotNull((await CombinedJob()).AuthorizationRevokedAtUtc);
        Assert.Equal(JobPublicationState.AlreadyPublished, result.State);
        Assert.Equal(snapshot.ContentHash, result.Receipt!.ContentHash);
        Assert.Equal((await CombinedJob()).FinishedAtUtc, result.Receipt.FinishedAtUtc);
        var before = await CombinedSnapshot();
        var live = await new AssignedExecutionObservationStore(H.Options()).ObserveAsync(
            new(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId) { Permit = permit }, a.ActorUserId, Token);
        Assert.Equal(LeaseOperationFailure.AuthorizationConflict, live.Failure); Assert.Null(live.Snapshot);
        var rejected = await new JobPublicationStore(H.Options()).PublishAsync(e with { Permit = null }, a.ActorUserId, snapshot, Token);
        Assert.Equal(JobPublicationFailure.AuthorizationConflict, rejected.Failure); Assert.Null(rejected.Receipt);
        Assert.Equal(before, await CombinedSnapshot()); Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Fact]
    public async Task ExperiencePermit_AutonomousStopAfterRevocation_DrainsOutstandingReader()
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files);
        var clock = new ManualSupervisionClock(); var copies = new BlockingReadCopies();
        using var client = new SupervisorOutstandingClient(); var calls = SupervisionProbe();
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);
        var core = new CoordinateAssignedExecutionService(new(new AssignedJobStartStore(H.Options())), store,
            new(store, client, NullLogger<ConsumeExecutionDocumentsService>.Instance),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()), store),
            new(new JobPublicationStore(H.Options())), H.Service());
        var task = new SuperviseAssignedExecutionService(new(calls), core, new(calls), new(calls), clock).ExecuteAsync(permit, Token);
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token); await SupervisionRead(clock.Scheduled);
            Assert.Equal(ExperienceRevocationState.Revoked, (await new ExperienceAuthorizationStore(H.Options())
                .RevokeAsync(permit.Admission.JobId, permit.Admission.ActorUserId, Token)).State);
            clock.Advance(TimeSpan.FromSeconds(30)); await client.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (client.Reader is null || client.Reader.CanRead)
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            Assert.False(task.IsCompleted); Assert.Equal(0, copies.Disposals); Assert.False(client.Reader!.CanRead);
        }
        finally { copies.Release.TrySetResult(); }
        var result = await task; if (client.Read is not null) await client.Read;
        Assert.Equal(AssignedSupervisionOutcome.Stopped, result.Outcome);
        Assert.Equal(2, copies.Disposals); Assert.Equal(1, client.Calls); Assert.Equal(0, calls.Renewals);
        var job = await CombinedJob(); Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.PublicationContentHash); Assert.Equal(0, files.Deletes);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/1.pdf"), Token));
    }
}
