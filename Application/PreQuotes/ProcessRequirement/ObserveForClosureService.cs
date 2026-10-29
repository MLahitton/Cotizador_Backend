using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record ExecutionClosureIdentity(Guid JobId, Guid RequirementId,
    Guid ExecutionRequestId, Guid ClaimOperationId);
// Observation only. The historic actor does not grant execution rights to the current owner.
public sealed record ClosureSnapshot(AssignedExecutionSnapshot Execution, Guid ClaimActorUserId,
    bool IsCurrent, int RequirementAttemptCount, Guid CorrelationId, string? ErrorCode);
public sealed record ClosureObservationResult(JobTerminationFailure Failure, ClosureSnapshot? Snapshot = null);
public interface IExecutionClosureObservationStore
{
    Task<ClosureObservationResult> ObserveForClosureAsync(ExecutionClosureIdentity identity,
        Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class ObserveForClosureService(IExecutionClosureObservationStore store)
{
    public async Task<ClosureObservationResult> ObserveForClosureAsync(ExecutionClosureIdentity identity,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Valid(identity, actorUserId)
            ? await store.ObserveForClosureAsync(identity, actorUserId, cancellationToken)
            : new(JobTerminationFailure.InvalidRequest);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static bool Valid(ExecutionClosureIdentity? r, Guid actor) => r is not null
        && actor != Guid.Empty && r.JobId != Guid.Empty && r.RequirementId != Guid.Empty
        && r.ExecutionRequestId != Guid.Empty && r.ClaimOperationId != Guid.Empty;
}
