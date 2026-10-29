namespace Application.PreQuotes.ProcessRequirement;

public sealed record AssignedJobExecution(Guid JobId, Guid ExecutionRequestId, Guid RequirementId,
    string LeaseOwner, long LeaseGeneration, Guid? ProcessingAttemptId = null)
{
    public ExperienceExecutionPermit? Permit { get; init; }
}

public enum AssignedJobStartState { Started, AlreadyStarted, Rejected, Indeterminate }
public enum AssignedJobStartFailure
{
    None, InvalidRequest, Unauthorized, InactiveUser, NotFound, InactiveProject, InactiveClient,
    IdentityConflict, ContextConflict, CancellationRequested, LeaseConflict, StateConflict,
    ActiveAttempt, InvalidDocuments, NotStarted, InconsistentEvidence, PersistenceError, AuthorizationConflict
}

public sealed record AssignedJobStartReceipt(Guid JobId, Guid ExecutionRequestId, Guid RequirementId,
    Guid ProcessingAttemptId, Guid CorrelationId, Guid ProjectId, Guid PreQuoteId, DateTimeOffset StartedAtUtc);

public sealed record AssignedJobStartResult(AssignedJobStartState State, AssignedJobStartFailure Failure,
    AssignedJobStartReceipt? Receipt = null)
{
    public static AssignedJobStartResult Reject(AssignedJobStartFailure failure) => new(AssignedJobStartState.Rejected, failure);
}

public interface IAssignedJobStartStore
{
    Task<AssignedJobStartResult> StartVerifiedAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken);
    Task<AssignedJobStartResult> StartAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken);
    Task<AssignedJobStartResult> RecognizeAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken);
}

// Deliberately has no pipeline/AI dependency and no production caller.
public sealed class StartAssignedJobService(IAssignedJobStartStore store)
{
    public Task<AssignedJobStartResult> StartVerifiedAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        Valid(execution, actorUserId) ? store.StartVerifiedAsync(execution, actorUserId, cancellationToken)
            : Task.FromResult(AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidRequest));

    public Task<AssignedJobStartResult> StartAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        Valid(execution, actorUserId) ? store.StartAsync(execution, actorUserId, cancellationToken)
            : Task.FromResult(AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidRequest));

    public Task<AssignedJobStartResult> RecognizeAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        Valid(execution, actorUserId) ? store.RecognizeAsync(execution, actorUserId, cancellationToken)
            : Task.FromResult(AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidRequest));

    private static bool Valid(AssignedJobExecution value, Guid actor) => value is not null
        && actor != Guid.Empty && value.JobId != Guid.Empty && value.ExecutionRequestId != Guid.Empty
        && value.RequirementId != Guid.Empty && value.ProcessingAttemptId != Guid.Empty
        && !string.IsNullOrWhiteSpace(value.LeaseOwner) && value.LeaseOwner.Length <= 200 && value.LeaseGeneration > 0;
}
