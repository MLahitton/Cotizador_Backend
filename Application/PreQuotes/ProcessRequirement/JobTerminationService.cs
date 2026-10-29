namespace Application.PreQuotes.ProcessRequirement;

public sealed record JobCancellationRequest(Guid JobId, Guid ExecutionRequestId, Guid RequirementId);
public sealed record ExecutionTermination(AssignedJobExecution Execution);
public enum JobTerminationState { CancellationRequested, CancellationConfirmed, Failed, AlreadyRecognized, Conflict, Indeterminate }
public enum JobTerminationFailure
{
    None, InvalidRequest, Unauthorized, InactiveUser, NotFound, InactiveClient, InactiveProject,
    IdentityConflict, ContextConflict, TerminalConflict, StateConflict, LeaseConflict,
    CancellationRequired, CancellationRequested, AttemptConflict, InconsistentEvidence, PersistenceError, AuthorizationConflict
}
public sealed record JobTerminationReceipt(Guid JobId, Guid ExecutionRequestId, Guid RequirementId,
    Guid? ProcessingAttemptId, Guid CorrelationId, string JobState, DateTimeOffset? CancellationRequestedAtUtc,
    DateTimeOffset? FinishedAtUtc, string? ErrorCode);
public sealed record JobTerminationResult(JobTerminationState State, JobTerminationFailure Failure,
    JobTerminationReceipt? Receipt = null);

public interface IJobTerminationStore
{
    Task<JobTerminationResult> FinalizeExecutionAsync(ExecutionTermination request, Guid actorUserId,
        string? errorCode, CancellationToken cancellationToken) => Task.FromResult(new JobTerminationResult(
            JobTerminationState.Conflict, JobTerminationFailure.AuthorizationConflict));
    Task<JobTerminationResult> RequestJobCancellationAsync(JobCancellationRequest request, Guid actorUserId, CancellationToken cancellationToken);
    Task<JobTerminationResult> ConfirmJobCancellationAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken);
    Task<JobTerminationResult> FinalizeJobFailedAsync(AssignedJobExecution execution, Guid actorUserId, string errorCode, CancellationToken cancellationToken);
}

// Closed subset of the existing ProcessRequirementCoreService failure codes, never exception text.
public static class JobFailureCodes
{
    public static bool IsAllowed(string code) => code is "REQUIREMENT_STORAGE_ERROR" or "AI2_SERVICE_UNAVAILABLE"
        or "AI2_TIMEOUT" or "AI2_REQUEST_REJECTED" or "AI_INVALID_RESPONSE" or "AI2_SERVICE_ERROR"
        or "REQUIREMENT_PERSISTENCE_ERROR";
}

public sealed class JobTerminationService(IJobTerminationStore store)
{
    public Task<JobTerminationResult> FinalizeExecutionAsync(ExecutionTermination request, Guid actorUserId,
        string? errorCode, CancellationToken cancellationToken) => request is not null
        && ValidExecution(request.Execution, actorUserId) && (errorCode is null || JobFailureCodes.IsAllowed(errorCode))
            ? store.FinalizeExecutionAsync(request, actorUserId, errorCode, cancellationToken) : Invalid();

    public Task<JobTerminationResult> RequestJobCancellationAsync(JobCancellationRequest request, Guid actorUserId, CancellationToken cancellationToken) =>
        request is not null && ValidIds(request.JobId, request.ExecutionRequestId, request.RequirementId, actorUserId)
            ? store.RequestJobCancellationAsync(request, actorUserId, cancellationToken) : Invalid();

    public Task<JobTerminationResult> ConfirmJobCancellationAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        ValidExecution(execution, actorUserId) ? store.ConfirmJobCancellationAsync(execution, actorUserId, cancellationToken) : Invalid();

    public Task<JobTerminationResult> FinalizeJobFailedAsync(AssignedJobExecution execution, Guid actorUserId, string errorCode, CancellationToken cancellationToken) =>
        ValidExecution(execution, actorUserId) && JobFailureCodes.IsAllowed(errorCode)
            ? store.FinalizeJobFailedAsync(execution, actorUserId, errorCode, cancellationToken) : Invalid();

    private static bool ValidIds(Guid job, Guid request, Guid requirement, Guid actor) =>
        job != Guid.Empty && request != Guid.Empty && requirement != Guid.Empty && actor != Guid.Empty;
    private static bool ValidExecution(AssignedJobExecution e, Guid actor) => e is not null
        && ValidIds(e.JobId, e.ExecutionRequestId, e.RequirementId, actor) && e.ProcessingAttemptId != Guid.Empty
        && !string.IsNullOrWhiteSpace(e.LeaseOwner) && e.LeaseOwner.Length <= 200 && e.LeaseGeneration > 0;
    private static Task<JobTerminationResult> Invalid() => Task.FromResult(new JobTerminationResult(
        JobTerminationState.Conflict, JobTerminationFailure.InvalidRequest));
}
