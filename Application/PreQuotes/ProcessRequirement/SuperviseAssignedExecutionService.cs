using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public enum AssignedSupervisionOutcome { NotStarted, CoordinatorCompleted, Stopped, Indeterminate }
public enum AssignedSupervisionStopReason
{
    None, ObservationUnavailable, IncompatibleObservation, CancellationRequested,
    RenewalNotConfirmed, UnexpectedCycleFailure, UnexpectedCoordinatorFailure
}
public sealed record AssignedSupervisionResult(AssignedSupervisionOutcome Outcome,
    LeaseOperationResult? Claim = null, AssignedExecutionResult? Coordinator = null,
    bool CoordinatorCancelled = false, AssignedSupervisionStopReason StopReason = AssignedSupervisionStopReason.None,
    LeaseOperationResult? LastRenewal = null, AssignedExecutionObservationResult? Observation = null);

// One exact invocation, not a worker. TimeProvider controls scheduling, never durable lease authority.
public sealed class SuperviseAssignedExecutionService(ClaimInitialLeaseService claim,
    CoordinateAssignedExecutionService coordinator, ObserveAssignedExecutionService observer,
    RenewActiveLeaseService renew, TimeProvider time)
{
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(30);

    public Task<AssignedSupervisionResult> ExecuteAsync(ExperienceExecutionPermit permit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        var a = permit.Admission;
        return ExecuteAsync(new ClaimInitialLease(a.JobId, a.RequirementId, a.ExecutionRequestId,
            a.ClaimOperationId, a.InstanceId) { Permit = permit }, a.ActorUserId, cancellationToken);
    }

    public async Task<AssignedSupervisionResult> ExecuteAsync(ClaimInitialLease request, Guid actor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LeaseOperationResult claimed;
        try { claimed = await claim.ClaimInitialV2Async(request, actor, cancellationToken); }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) { return new(AssignedSupervisionOutcome.Indeterminate); }
        cancellationToken.ThrowIfCancellationRequested();
        if (claimed.State != LeaseOperationState.Claimed)
            return new(claimed.State == LeaseOperationState.Indeterminate ? AssignedSupervisionOutcome.Indeterminate
                : AssignedSupervisionOutcome.NotStarted, claimed);
        if (!MatchesClaim(claimed, request, actor))
            return new(AssignedSupervisionOutcome.NotStarted, claimed);

        var assigned = claimed.Receipt!;
        var origin = time.GetTimestamp();
        var execution = new AssignedJobExecution(assigned.JobId, assigned.ExecutionRequestId, assigned.RequirementId,
            assigned.LeaseOwner, assigned.LeaseGeneration) { Permit = request.Permit };
        using var control = new AssignedExecutionControl();
        using var cycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = coordinator.ExecuteAsync(execution, actor, control, cancellationToken);
        var completion = JoinCoordinatorAsync(running, control, cycleCancellation);
        var cycling = CycleAsync(control, assigned, actor, origin, cycleCancellation.Token, cancellationToken, request.Permit);
        // Both tasks contain their failures. Neither control nor its tokens are disposed before both drain.
        var finished = await completion;
        var cycle = await cycling;
        cancellationToken.ThrowIfCancellationRequested();
        var observation = await ObserveAsync(assigned, actor, cancellationToken, request.Permit);
        cancellationToken.ThrowIfCancellationRequested();
        var reason = finished.Faulted ? AssignedSupervisionStopReason.UnexpectedCoordinatorFailure : cycle.Reason;
        var confirmedTerminal = finished.Result?.Outcome is AssignedExecutionOutcome.Published
            or AssignedExecutionOutcome.Cancelled or AssignedExecutionOutcome.Failed;
        return new(confirmedTerminal || reason == AssignedSupervisionStopReason.None && finished.Result is not null
                ? AssignedSupervisionOutcome.CoordinatorCompleted : AssignedSupervisionOutcome.Stopped,
            claimed, finished.Result, finished.Cancelled, reason, cycle.Renewal, observation);
    }

    private sealed record Completion(AssignedExecutionResult? Result = null, bool Cancelled = false, bool Faulted = false);
    private static async Task<Completion> JoinCoordinatorAsync(Task<AssignedExecutionResult> running,
        AssignedExecutionControl control, CancellationTokenSource cycleCancellation)
    {
        try { return new(await running); }
        catch (OperationCanceledException) { return new(Cancelled: true); }
        catch (Exception) { control.Stop(); return new(Faulted: true); }
        finally
        {
            // Cancellation callbacks must not replace a known coordinator result or prevent joining the cycle.
            try { cycleCancellation.Cancel(); }
            catch (AggregateException) { }
        }
    }

    private sealed record CycleResult(AssignedSupervisionStopReason Reason, LeaseOperationResult? Renewal = null);
    private async Task<CycleResult> CycleAsync(AssignedExecutionControl control, JobLeaseOperation assigned,
        Guid actor, long origin, CancellationToken ct, CancellationToken caller, ExperienceExecutionPermit? permit)
    {
        LeaseOperationResult? last = null;
        CycleResult Stop(AssignedSupervisionStopReason reason) { control.Stop(); return new(reason, last); }
        try
        {
            var execution = await control.Started.WaitAsync(ct);
            if (execution is null) return new(AssignedSupervisionStopReason.None);
            if (execution.JobId != assigned.JobId || execution.RequirementId != assigned.RequirementId
                || execution.ExecutionRequestId != assigned.ExecutionRequestId || execution.LeaseOwner != assigned.LeaseOwner
                || execution.LeaseGeneration != assigned.LeaseGeneration || execution.ProcessingAttemptId is null)
                return Stop(AssignedSupervisionStopReason.IncompatibleObservation);
            var remaining = Cadence - time.GetElapsedTime(origin);
            while (true)
            {
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, time, ct);
                ct.ThrowIfCancellationRequested();
                var observed = await observer.ObserveAsync(new(assigned.JobId, assigned.RequirementId,
                    assigned.ExecutionRequestId, assigned.OperationId) { Permit = permit }, actor, ct);
                ct.ThrowIfCancellationRequested();
                if (observed.State == AssignedExecutionObservationState.Unavailable)
                    return Stop(AssignedSupervisionStopReason.ObservationUnavailable);
                if (observed.Snapshot?.CancellationRequestedAtUtc is not null)
                    return Stop(AssignedSupervisionStopReason.CancellationRequested);
                if (observed.State != AssignedExecutionObservationState.Observed
                    || !MatchesSnapshot(observed.Snapshot, assigned, execution, actor))
                    return Stop(AssignedSupervisionStopReason.IncompatibleObservation);
                // Frozen inputs for exactly one operation; no CAS refresh/retry after any failure.
                var input = new RenewActiveLease(assigned.JobId, assigned.RequirementId, assigned.ExecutionRequestId,
                    Guid.NewGuid(), assigned.OperationId, assigned.LeaseOwner, assigned.LeaseGeneration,
                    observed.Snapshot!.LeaseExpiresAtUtc!.Value, execution.ProcessingAttemptId) { Permit = permit };
                ct.ThrowIfCancellationRequested();
                last = await renew.RenewActiveV2Async(input, actor, ct);
                ct.ThrowIfCancellationRequested();
                if (!MatchesRenewal(last, input, assigned, actor))
                    return Stop(last.Failure == LeaseOperationFailure.CancellationRequested
                        ? AssignedSupervisionStopReason.CancellationRequested : AssignedSupervisionStopReason.RenewalNotConfirmed);
                // No catch-up ticks, including an applied operation with no effective extension.
                remaining = Cadence;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (caller.IsCancellationRequested) control.Stop();
            return new(AssignedSupervisionStopReason.None, last);
        }
        catch (Exception) { return Stop(AssignedSupervisionStopReason.UnexpectedCycleFailure); }
    }

    private async Task<AssignedExecutionObservationResult> ObserveAsync(JobLeaseOperation assigned, Guid actor, CancellationToken ct,
        ExperienceExecutionPermit? permit)
    {
        try { return await observer.ObserveAsync(new(assigned.JobId, assigned.RequirementId, assigned.ExecutionRequestId, assigned.OperationId)
            { Permit = permit }, actor, ct); }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception) { return new(AssignedExecutionObservationState.Unavailable); }
    }

    private static bool MatchesSnapshot(AssignedExecutionSnapshot? s, JobLeaseOperation claim, AssignedJobExecution e, Guid actor) =>
        s is not null && s.JobId == claim.JobId && s.RequirementId == claim.RequirementId
        && s.ExecutionRequestId == claim.ExecutionRequestId && s.ClaimOperationId == claim.OperationId && s.ActorUserId == actor
        && s.JobState == RequirementProcessingJobState.Running && !s.IsTerminal && s.CancellationRequestedAtUtc is null
        && s.ProcessingAttemptId == e.ProcessingAttemptId && s.AttemptCount == 1 && s.AttemptState == DocumentProcessingState.Processing
        && s.AssignedLeaseOwner == claim.LeaseOwner && s.CurrentLeaseOwner == claim.LeaseOwner
        && s.LeaseGeneration == claim.LeaseGeneration && s.AssignedAtUtc == claim.AssignedAtUtc
        && s.FinishedAtUtc is null && s.LeaseExpiresAtUtc > s.ObservedAtUtc && s.Lease == ObservedExecutionLease.Live
        && s.Authorization == ObservedExecutionAuthorization.ActiveContext && s.RequirementStatus == RequirementStatus.Processing;

    private static bool MatchesClaim(LeaseOperationResult result, ClaimInitialLease input, Guid actor)
    {
        var c = result.Receipt;
        return c is not null && c.Kind == JobLeaseOperationKind.Claim && c.OperationId == input.OperationId
            && c.ClaimOperationId == input.OperationId && c.JobId == input.JobId && c.RequirementId == input.RequirementId
            && c.ExecutionRequestId == input.ExecutionRequestId && c.ActorUserId == actor && c.LeaseGeneration == 1
            && c.LeaseOwner == $"exec:{input.InstanceId:D}:{input.OperationId:D}"
            && result.Observation is { Availability: LeaseAvailability.Live, JobState: RequirementProcessingJobState.Running,
                ProcessingAttemptId: null } o && o.CurrentExpiresAtUtc == c.ExpiresAtUtc && c.ExpiresAtUtc > o.ObservedAtUtc;
    }

    private static bool MatchesRenewal(LeaseOperationResult result, RenewActiveLease input, JobLeaseOperation claim, Guid actor)
    {
        var r = result.Receipt;
        if (result.State is not (LeaseOperationState.Renewed or LeaseOperationState.AlreadyApplied) || r is null
            || r.Kind != JobLeaseOperationKind.Renew || r.OperationId != input.OperationId || r.ClaimOperationId != input.ClaimOperationId
            || r.JobId != input.JobId || r.RequirementId != input.RequirementId || r.ExecutionRequestId != input.ExecutionRequestId
            || r.ActorUserId != actor || r.LeaseOwner != input.LeaseOwner || r.LeaseGeneration != input.LeaseGeneration
            || r.AssignedAtUtc != claim.AssignedAtUtc || r.ExpectedLeaseExpiresAtUtc != input.ExpectedLeaseExpiresAtUtc
            || r.ExpectedProcessingAttemptId != input.ExpectedProcessingAttemptId
            || result.Observation is not { Availability: LeaseAvailability.Live, JobState: RequirementProcessingJobState.Running } o
            || o.ProcessingAttemptId != input.ExpectedProcessingAttemptId || o.CurrentExpiresAtUtc != r.ExpiresAtUtc
            || r.ExpiresAtUtc <= o.ObservedAtUtc) return false;
        try
        {
            _ = JobLeaseOperation.CreateRenew(r.OperationId, r.JobId, r.RequirementId, r.ExecutionRequestId, r.ActorUserId,
                r.ClaimOperationId, r.LeaseOwner, r.LeaseGeneration, r.PolicyVersion, r.ExpectedLeaseExpiresAtUtc!.Value,
                r.ExpectedProcessingAttemptId, r.AssignedAtUtc, r.AppliedAtUtc, r.ExpiresAtUtc);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
