using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record ClaimInitialLease(Guid JobId, Guid RequirementId, Guid ExecutionRequestId,
    Guid OperationId, Guid InstanceId)
{
    public ExperienceExecutionPermit? Permit { get; init; }
}
public enum LeaseOperationState { Claimed, AlreadyApplied, Rejected, Conflict, PersistenceError, Indeterminate, Renewed }
public enum LeaseOperationFailure
{
    None, InvalidRequest, NotFound, InactiveUser, InactiveContext, WrongVersion,
    CancellationRequested, Expired, Terminal, IdentityMismatch, AlreadyOwned, AttemptConflict, ReceiptConflict, StaleLease,
    AuthorizationConflict
}
public enum LeaseAvailability { Live, Expired, Terminal, CancellationRequested, ContextChanged, Superseded }
public sealed record LeaseObservation(RequirementProcessingJobState JobState, Guid? ProcessingAttemptId,
    DateTimeOffset ObservedAtUtc, DateTimeOffset? CurrentExpiresAtUtc, LeaseAvailability Availability);
public sealed record LeaseOperationResult(LeaseOperationState State, LeaseOperationFailure Failure = LeaseOperationFailure.None,
    JobLeaseOperation? Receipt = null, LeaseObservation? Observation = null);

public interface IInitialLeaseClaimStore
{
    Task<LeaseOperationResult> ClaimInitialV2Async(ClaimInitialLease request, Guid actorUserId, CancellationToken ct);
}

// Explicit backend invocation only; no identity delegation, scheduling or execution pipeline.
public sealed class ClaimInitialLeaseService(IInitialLeaseClaimStore store)
{
    public async Task<LeaseOperationResult> ClaimInitialV2Async(ClaimInitialLease request, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = Valid(request, actorUserId)
            ? await store.ClaimInitialV2Async(request, actorUserId, ct)
            : new(LeaseOperationState.Rejected, LeaseOperationFailure.InvalidRequest);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public static bool Valid(ClaimInitialLease? request, Guid actor) => request is not null && actor != Guid.Empty
        && request.JobId != Guid.Empty && request.RequirementId != Guid.Empty && request.ExecutionRequestId != Guid.Empty
        && request.OperationId != Guid.Empty && request.InstanceId != Guid.Empty;
}
