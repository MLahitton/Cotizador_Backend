using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private sealed class PermitCommitGate : DbTransactionInterceptor
    {
        internal readonly TaskCompletionSource<int> Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = Signal();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ExperiencePermit_RevokeVersusClaimStart_PostgreSqlOrder(bool start, bool revokeFirst)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
        var execution = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId,
            $"exec:{a.InstanceId:D}:{a.ClaimOperationId:D}", 1) { Permit = permit };
        if (start) Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(request, a.ActorUserId, Token)).State);
        var gate = new PermitCommitGate(); var probe = new JobProtocolTestSupport.QuoteProbe();
        async Task<object> Execute(params IInterceptor[] hooks) => start
            ? await new AssignedJobStartStore(H.Options(hooks)).StartVerifiedAsync(execution, a.ActorUserId, Token)
            : await CombinedClaim(hooks).ClaimInitialV2Async(request, a.ActorUserId, Token);
        async Task<object> Revoke(params IInterceptor[] hooks) => await new ExperienceAuthorizationStore(H.Options(hooks))
            .RevokeAsync(a.JobId, a.ActorUserId, Token);
        var first = revokeFirst ? Revoke(gate) : Execute(gate);
        Task<object>? second = null;
        object? firstResult = null, secondResult = null;
        try
        {
            var winner = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            second = revokeFirst ? Execute(probe) : Revoke(probe);
            var loser = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"Permit race start={start}, revokeFirst={revokeFirst}, winner={winner}, blocked={loser}; pg_blocking_pids observed.");
        }
        finally
        {
            gate.Release.TrySetResult(); firstResult = await first;
            if (second is not null) secondResult = await second;
        }
        var operation = revokeFirst ? secondResult : firstResult;
        var revoked = Assert.IsType<ExperienceRevocationResult>(revokeFirst ? firstResult : secondResult);
        Assert.Equal(ExperienceRevocationState.Revoked, revoked.State);
        if (start)
        {
            var result = Assert.IsType<AssignedJobStartResult>(operation);
            Assert.Equal(revokeFirst ? AssignedJobStartState.Rejected : AssignedJobStartState.Started, result.State);
            Assert.Equal(revokeFirst ? AssignedJobStartFailure.AuthorizationConflict : AssignedJobStartFailure.None, result.Failure);
        }
        else
        {
            var result = Assert.IsType<LeaseOperationResult>(operation);
            Assert.Equal(revokeFirst ? LeaseOperationState.Rejected : LeaseOperationState.Claimed, result.State);
            Assert.Equal(revokeFirst ? LeaseOperationFailure.AuthorizationConflict : LeaseOperationFailure.None, result.Failure);
        }
        var job = await CombinedJob();
        Assert.Equal(start && !revokeFirst ? 1 : 0, job.AttemptCount);
        Assert.NotNull(job.AuthorizationRevokedAtUtc); Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.FinishedAtUtc);
        Assert.Equal(0, files.Opens);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_RevokeDuringCopyOrProvider_NoPublication(bool provider)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files);
        var copies = new Copies(); var client = new ConsumingClient(); var entered = Signal(); var release = Signal();
        async Task Pause() { copies.AfterCopy = null; entered.TrySetResult(); await release.Task.WaitAsync(Token); }
        if (provider) client.DuringRead = Pause; else copies.AfterCopy = Pause;
        var calls = SupervisionProbe();
        var supervisor = new SuperviseAssignedExecutionService(new(calls), PermitCoordinator(files, copies, client),
            new(calls), new(calls), new ManualSupervisionClock());
        var task = supervisor.ExecuteAsync(permit, Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            var revoked = await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(permit.Admission.JobId, permit.Admission.ActorUserId, Token);
            Assert.Equal(ExperienceRevocationState.Revoked, revoked.State);
            Assert.All(copies.Created, x => Assert.False(x.Disposed));
        }
        finally { release.TrySetResult(); }
        var result = await task;
        Assert.Equal(AssignedExecutionOutcome.Rejected, result.Coordinator!.Outcome);
        Assert.Equal(provider ? AssignedExecutionStage.Preparation : AssignedExecutionStage.Documents, result.Coordinator.Stage);
        Assert.Equal(ExecutionDocumentPreparationFailure.AuthorizationConflict, result.Coordinator.DocumentFailure);
        var job = await CombinedJob();
        Assert.Equal(RequirementProcessingJobState.Running, job.State); Assert.Null(job.FinishedAtUtc);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.PublicationContentHash);
        Assert.Equal(provider ? 1 : 0, client.Calls); Released(files, copies, client);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_OwnCommitRecognizedAfterRevocation_NoNewStage(bool start)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
        var execution = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId,
            $"exec:{a.InstanceId:D}:{a.ClaimOperationId:D}", 1) { Permit = permit };
        if (start) Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(request, a.ActorUserId, Token)).State);
        var lost = new SupervisionAfterCommit(async () =>
        {
            Assert.Equal(ExperienceRevocationState.Revoked, (await new ExperienceAuthorizationStore(H.Options()).RevokeAsync(a.JobId, a.ActorUserId, Token)).State);
            throw new IOException("Lost response after real commit and revocation.");
        });
        if (start)
        {
            var result = await new AssignedJobStartStore(H.Options(lost)).StartVerifiedAsync(execution, a.ActorUserId, Token);
            Assert.Equal(AssignedJobStartState.AlreadyStarted, result.State);
            execution = execution with { ProcessingAttemptId = result.Receipt!.ProcessingAttemptId };
            var recognition = new AssignedJobStartStore(H.Options(), RequirementProcessingJob.PublicationV2);
            Assert.Equal(AssignedJobStartFailure.AuthorizationConflict,
                (await recognition.RecognizeAsync(execution with { Permit = null }, a.ActorUserId, Token)).Failure);
            var copies = new Copies();
            Assert.Equal(ExecutionDocumentPreparationFailure.AuthorizationConflict,
                (await Store(files, copies).PrepareAsync(execution, a.ActorUserId, Token)).Failure);
        }
        else
        {
            Assert.Equal(LeaseOperationState.AlreadyApplied, (await CombinedClaim(lost).ClaimInitialV2Async(request, a.ActorUserId, Token)).State);
            Assert.Equal(AssignedJobStartFailure.AuthorizationConflict,
                (await H.StartService().StartVerifiedAsync(execution, a.ActorUserId, Token)).Failure);
        }
        Assert.Equal(0, files.Opens);
        Assert.Equal(RequirementProcessingJobState.Running, (await CombinedJob()).State);
        Assert.Equal(JobTerminationState.CancellationRequested, (await H.Service().RequestJobCancellationAsync(
            new(a.JobId, a.ExecutionRequestId, a.RequirementId), a.ActorUserId, Token)).State);
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await H.Service().ConfirmJobCancellationAsync(execution, a.ActorUserId, Token)).State);
        if (start) Assert.Equal(AssignedJobStartState.AlreadyStarted,
            (await new AssignedJobStartStore(H.Options(), RequirementProcessingJob.PublicationV2)
                .RecognizeAsync(execution, a.ActorUserId, Token)).State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExperiencePermit_CallerCancelledAfterCommit_PreservesOriginalToken(bool start)
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files); var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
        var execution = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId,
            $"exec:{a.InstanceId:D}:{a.ClaimOperationId:D}", 1) { Permit = permit };
        if (start) Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(request, a.ActorUserId, Token)).State);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var lost = new CommitHook(() => { caller.Cancel(); throw new IOException("Lost committed response."); });
        var error = start
            ? await Assert.ThrowsAsync<OperationCanceledException>(() => new AssignedJobStartStore(H.Options(lost)).StartVerifiedAsync(execution, a.ActorUserId, caller.Token))
            : await Assert.ThrowsAsync<OperationCanceledException>(() => CombinedClaim(lost).ClaimInitialV2Async(request, a.ActorUserId, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        var job = await CombinedJob();
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Equal(start ? 1 : 0, job.AttemptCount); Assert.Null(job.CancellationRequestedAtUtc);
        Assert.Equal(0, files.Opens);
    }

    [Fact]
    public async Task ExperiencePermit_StartExpiresAfterSave_FinalGuardRollsBack()
    {
        await using var files = new Files(); var (_, permit) = await PermitSetup(files, 10); var a = permit.Admission;
        var request = new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId, a.ClaimOperationId, a.InstanceId) { Permit = permit };
        Assert.Equal(LeaseOperationState.Claimed, (await CombinedClaim().ClaimInitialV2Async(request, a.ActorUserId, Token)).State);
        var execution = new AssignedJobExecution(a.JobId, a.ExecutionRequestId, a.RequirementId,
            $"exec:{a.InstanceId:D}:{a.ClaimOperationId:D}", 1) { Permit = permit };
        var before = await CombinedSnapshot();
        var saved = new Saved(async _ =>
        {
            var now = await PermitWaitUntilExpired(a.ExpiresAtUtc);
            output.WriteLine($"Authorization expired after SaveChanges: DB={now:O}, expiry={a.ExpiresAtUtc:O}.");
        });
        var result = await new AssignedJobStartStore(H.Options(saved)).StartVerifiedAsync(execution, a.ActorUserId, Token);
        Assert.Equal(AssignedJobStartFailure.LeaseConflict, result.Failure); Assert.Null(result.Receipt);
        Assert.Equal(before, await CombinedSnapshot());
        Assert.Equal(AssignedJobStartFailure.AuthorizationConflict,
            (await H.StartService().StartVerifiedAsync(execution, a.ActorUserId, Token)).Failure);
    }
}
