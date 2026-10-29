using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record AssignedExecutionObservationRequest(Guid JobId, Guid RequirementId,
    Guid ExecutionRequestId, Guid ClaimOperationId)
{
    public ExperienceExecutionPermit? Permit { get; init; }
}
public enum AssignedExecutionObservationState { Observed, Rejected, Unavailable }
public enum ObservedExecutionLease { Live, Expired, Released }
public enum ObservedExecutionAuthorization { ActiveContext, ContextChanged }
// Values only: this is a coherent observation, not an execution permit or commit acknowledgment.
public sealed record AssignedExecutionSnapshot(Guid JobId, Guid RequirementId, Guid ExecutionRequestId,
    Guid ClaimOperationId, Guid ActorUserId, RequirementProcessingJobState JobState, bool IsTerminal,
    DateTimeOffset? CancellationRequestedAtUtc, Guid? ProcessingAttemptId, int AttemptCount,
    DocumentProcessingState? AttemptState, DocumentProcessingOutcome? AttemptOutcome,
    string AssignedLeaseOwner, string? CurrentLeaseOwner, long LeaseGeneration,
    DateTimeOffset AssignedAtUtc, DateTimeOffset? LeaseExpiresAtUtc, DateTimeOffset? FinishedAtUtc,
    DateTimeOffset ObservedAtUtc, ObservedExecutionLease Lease,
    ObservedExecutionAuthorization Authorization, RequirementStatus RequirementStatus);
public sealed record AssignedExecutionObservationResult(AssignedExecutionObservationState State,
    LeaseOperationFailure Failure = LeaseOperationFailure.None, AssignedExecutionSnapshot? Snapshot = null);

public interface IAssignedExecutionObservationStore
{
    Task<AssignedExecutionObservationResult> ObserveAsync(AssignedExecutionObservationRequest request,
        Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class ObserveAssignedExecutionService(IAssignedExecutionObservationStore store)
{
    public async Task<AssignedExecutionObservationResult> ObserveAsync(AssignedExecutionObservationRequest request,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Valid(request, actorUserId)
            ? await store.ObserveAsync(request, actorUserId, cancellationToken)
            : new(AssignedExecutionObservationState.Rejected, LeaseOperationFailure.InvalidRequest);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static bool Valid(AssignedExecutionObservationRequest? r, Guid actor) => r is not null
        && actor != Guid.Empty && r.JobId != Guid.Empty && r.RequirementId != Guid.Empty
        && r.ExecutionRequestId != Guid.Empty && r.ClaimOperationId != Guid.Empty;
}
