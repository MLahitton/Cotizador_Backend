using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_CoordinatorFinalizer_ValidPermitAndBusinessRequest(bool cancellation)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var copies = new Copies(); var client = new ConsumingClient();
        client.Action = async (request, _) =>
        {
            if (!cancellation) throw new IOException("Simulated provider unavailable.");
            Assert.Equal(JobTerminationState.CancellationRequested, (await H.Service().RequestJobCancellationAsync(
                new(a.JobId, a.ExecutionRequestId, a.RequirementId), a.ActorUserId, Token)).State);
            return ConsumingClient.Response(request);
        };
        var calls = SupervisionProbe();
        var result = await new SuperviseAssignedExecutionService(new(calls), PermitCoordinator(files, copies, client),
            new(calls), new(calls), new ManualSupervisionClock()).ExecuteAsync(permit, Token);
        Assert.Equal(cancellation ? AssignedExecutionOutcome.Cancelled : AssignedExecutionOutcome.Failed, result.Coordinator!.Outcome);
        Assert.Equal(AssignedExecutionStage.Termination, result.Coordinator.Stage);
        var job = await CombinedJob();
        Assert.Equal(cancellation ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Failed, job.State);
        Assert.Equal(cancellation ? null : "AI2_SERVICE_UNAVAILABLE", job.LastErrorCode);
        Assert.Equal(cancellation, job.CancellationRequestedAtUtc.HasValue);
        Assert.Null(job.AuthorizationRevokedAtUtc); Assert.Null(job.PublicationContentHash);
        Assert.Equal(1, client.Calls); Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_ClaimCannotReplaceAdmittedIdentities(bool instance)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var before = await CombinedSnapshot(); var writes = new CombinedCommands();
        var result = await CombinedClaim(writes).ClaimInitialV2Async(new(a.JobId, a.RequirementId, a.ExecutionRequestId,
            instance ? a.ClaimOperationId : Guid.NewGuid(), instance ? Guid.NewGuid() : a.InstanceId) { Permit = permit }, a.ActorUserId, Token);
        Assert.Equal(LeaseOperationFailure.AuthorizationConflict, result.Failure); Assert.Null(result.Receipt);
        Assert.Equal(0, writes.Writes); Assert.Equal(before, await CombinedSnapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_NewOwnerCannotExecute_CanExplicitlyClose(bool expiredLease)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
        var claim = await CombinedClaim().ClaimInitialV2Async(request, a.ActorUserId, Token);
        Assert.Equal(LeaseOperationState.Claimed, claim.State);
        var e = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId, claim.Receipt!.LeaseOwner, 1) { Permit = permit };
        var next = await NextStartOwner(true);
        var before = await CombinedSnapshot();
        Assert.Equal(AssignedJobStartFailure.NotFound, (await H.StartService().StartVerifiedAsync(e, a.ActorUserId, Token)).Failure);
        Assert.Equal(AssignedJobStartFailure.AuthorizationConflict, (await H.StartService().StartVerifiedAsync(e, next, Token)).Failure);
        Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(ExperienceRevocationState.Revoked,
            (await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(a.JobId, next, Token)).State);
        Assert.Equal(JobTerminationState.CancellationRequested,
            (await H.Service().RequestJobCancellationAsync(new(a.JobId, a.ExecutionRequestId, a.RequirementId), next, Token)).State);
        Assert.Equal(JobTerminationFailure.None,
            (await ClosureObserver().ObserveForClosureAsync(new(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId), next, Token)).Failure);
        if (expiredLease)
        {
            var expiry = claim.Receipt.AssignedAtUtc.AddTicks(10);
            Assert.True(await H.Now() > expiry);
            await using (var db = fixture.CreateDbContext())
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expiry} WHERE id={a.JobId}", Token);
            var job = await CombinedJob();
            var closed = await new CloseExpiredCancellationService(new ExpiredCancellationStore(H.Options())).CloseExpiredCancellationAsync(
                new(new(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId), Guid.NewGuid(), e.LeaseOwner, 1,
                    claim.Receipt.AssignedAtUtc, null, job.CancellationRequestedAtUtc!.Value, expiry), next, Token);
            Assert.Equal(JobTerminationState.CancellationConfirmed, closed.Result.State);
            Assert.Equal(next, closed.Receipt!.ClosedByActorUserId);
        }
        else
        {
            // Explicit owner Confirm, not the execution finalizer. A remains a separate caller with its drain proof.
            Assert.Equal(JobTerminationState.CancellationConfirmed,
                (await H.Service().ConfirmJobCancellationAsync(e with { Permit = null }, next, Token)).State);
        }
        await CombinedReceiptUnchanged(claim.Receipt);
        Assert.Equal(RequirementProcessingJobState.Cancelled, (await CombinedJob()).State);
        Assert.Equal(0, files.Opens);
    }
}
