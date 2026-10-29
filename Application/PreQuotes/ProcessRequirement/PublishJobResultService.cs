using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public enum JobPublicationState { Published, AlreadyPublished, Conflict, InvalidResult, PersistenceError, Indeterminate }
public enum JobPublicationFailure
{
    None, InvalidRequest, Unauthorized, InactiveUser, NotFound, InactiveClient, InactiveProject,
    IdentityConflict, ContextConflict, AttemptConflict, StateConflict, TerminalConflict,
    LeaseConflict, CancellationRequested, ResultMismatch, ManifestMismatch, InconsistentEvidence, PersistenceError, AuthorizationConflict
}
public sealed record JobPublicationReceipt(Guid JobId, Guid ExecutionRequestId, Guid RequirementId,
    Guid ProcessingAttemptId, Guid CorrelationId, Guid ExtractionId, Guid ProposalId,
    DocumentProcessingOutcome Outcome, DateTimeOffset FinishedAtUtc, string ContractVersion, string ContentHash);
public sealed record JobPublicationResult(JobPublicationState State, JobPublicationFailure Failure,
    JobPublicationReceipt? Receipt = null);
public interface IJobPublicationStore
{
    Task<JobPublicationResult> PublishAsync(AssignedJobExecution execution, Guid actorUserId,
        PreparedJobPublication snapshot, CancellationToken cancellationToken);
}

public sealed class PublishJobResultService(IJobPublicationStore store)
{
    public Task<JobPublicationResult> PublishAsync(AssignedJobExecution e, Guid actorUserId,
        PreparedJobPublication snapshot, CancellationToken cancellationToken)
    {
        if (e is null || e.JobId == Guid.Empty || e.ExecutionRequestId == Guid.Empty || e.RequirementId == Guid.Empty
            || !e.ProcessingAttemptId.HasValue || e.ProcessingAttemptId == Guid.Empty || actorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(e.LeaseOwner) || e.LeaseOwner.Length > 200 || e.LeaseGeneration <= 0)
            return Task.FromResult(new JobPublicationResult(JobPublicationState.Conflict, JobPublicationFailure.InvalidRequest));
        if (snapshot is null)
            return Task.FromResult(new JobPublicationResult(JobPublicationState.InvalidResult, JobPublicationFailure.InvalidRequest));
        return store.PublishAsync(e, actorUserId, snapshot, cancellationToken);
    }
}
