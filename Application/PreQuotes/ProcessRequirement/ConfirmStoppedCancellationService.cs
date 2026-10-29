using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record LiveCancellation(ExecutionClosureIdentity Identity, string LeaseOwner,
    long LeaseGeneration, DateTimeOffset AssignedAtUtc, Guid? ExpectedProcessingAttemptId,
    DateTimeOffset ExpectedCancellationRequestedAtUtc);

public sealed class ConfirmStoppedCancellationService(ObserveForClosureService observer,
    JobTerminationService termination)
{
    public async Task<JobTerminationResult> ConfirmStoppedCancellationAsync(LiveCancellation request,
        LocalDrainCompletion? local, Guid actorUserId, CancellationToken actionCt)
    {
        actionCt.ThrowIfCancellationRequested();
        if (!Valid(request, actorUserId)) return Reject(JobTerminationFailure.InvalidRequest);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(actionCt);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var observed = await observer.ObserveForClosureAsync(request.Identity, actorUserId, budget.Token);
            if (observed.Failure != JobTerminationFailure.None || observed.Snapshot is null)
                return Reject(observed.Failure == JobTerminationFailure.None
                    ? JobTerminationFailure.InconsistentEvidence : observed.Failure);
            var s = observed.Snapshot;
            var e = s.Execution;
            if (e.JobId != request.Identity.JobId || e.RequirementId != request.Identity.RequirementId
                || e.ExecutionRequestId != request.Identity.ExecutionRequestId || e.ClaimOperationId != request.Identity.ClaimOperationId
                || e.ActorUserId != actorUserId || e.AssignedLeaseOwner != request.LeaseOwner
                || e.LeaseGeneration != request.LeaseGeneration || e.AssignedAtUtc != request.AssignedAtUtc
                || e.ProcessingAttemptId != request.ExpectedProcessingAttemptId)
                return Reject(JobTerminationFailure.IdentityConflict);
            if (e.JobState is RequirementProcessingJobState.Succeeded or RequirementProcessingJobState.Failed)
                return Reject(JobTerminationFailure.TerminalConflict);
            if (e.CancellationRequestedAtUtc is null) return Reject(JobTerminationFailure.CancellationRequired);
            if (e.CancellationRequestedAtUtc != request.ExpectedCancellationRequestedAtUtc)
                return Reject(JobTerminationFailure.IdentityConflict);
            if (!e.IsTerminal)
            {
                if (e.JobState != RequirementProcessingJobState.Running || !s.IsCurrent
                    || e.RequirementStatus != (e.ProcessingAttemptId is null ? RequirementStatus.Pending : RequirementStatus.Processing))
                    return Reject(JobTerminationFailure.StateConflict);
                if (e.ProcessingAttemptId is null ? s.RequirementAttemptCount != 0
                    : e.AttemptCount != 1 || e.AttemptState != DocumentProcessingState.Processing)
                    return Reject(JobTerminationFailure.AttemptConflict);
                if (e.Lease != ObservedExecutionLease.Live) return Reject(JobTerminationFailure.LeaseConflict);
                if (local is null || local.Identity != request.Identity || local.LeaseOwner != request.LeaseOwner
                    || local.LeaseGeneration != request.LeaseGeneration || local.AssignedAtUtc != request.AssignedAtUtc)
                    return Reject(JobTerminationFailure.ContextConflict);
            }
            else if (e.JobState != RequirementProcessingJobState.Cancelled)
                return Reject(JobTerminationFailure.TerminalConflict);

            // Read scopes are closed. Confirm revalidates current authority, attempt, request and live guard;
            // it does not receive Claim or expected dates. No retry or independent observer is added here.
            budget.Token.ThrowIfCancellationRequested();
            return await termination.ConfirmJobCancellationAsync(new(e.JobId, e.ExecutionRequestId,
                e.RequirementId, request.LeaseOwner, e.LeaseGeneration, e.ProcessingAttemptId), actorUserId, budget.Token);
        }
        catch (Exception) when (actionCt.IsCancellationRequested) { throw new OperationCanceledException(actionCt); }
        catch (Exception) { return new(JobTerminationState.Indeterminate, JobTerminationFailure.InconsistentEvidence); }
        finally { actionCt.ThrowIfCancellationRequested(); }
    }

    private static JobTerminationResult Reject(JobTerminationFailure failure) => new(JobTerminationState.Conflict, failure);
    private static bool Valid(LiveCancellation? r, Guid actor) => r is not null
        && ObserveForClosureService.Valid(r.Identity, actor) && r.LeaseGeneration == 1
        && !string.IsNullOrWhiteSpace(r.LeaseOwner) && r.LeaseOwner.Length <= 200
        && r.ExpectedProcessingAttemptId != Guid.Empty && Microseconds(r.AssignedAtUtc)
        && Microseconds(r.ExpectedCancellationRequestedAtUtc) && r.ExpectedCancellationRequestedAtUtc >= r.AssignedAtUtc;
    private static bool Microseconds(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time.Ticks % 10 == 0;
}
