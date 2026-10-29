using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record ExpiredCancellation(ExecutionClosureIdentity Identity, Guid OperationId,
    string LeaseOwner, long LeaseGeneration, DateTimeOffset AssignedAtUtc,
    Guid? ExpectedProcessingAttemptId, DateTimeOffset ExpectedCancellationRequestedAtUtc,
    DateTimeOffset ExpectedLeaseExpiresAtUtc);

public sealed record ExpiredClosureResult(JobTerminationResult Result, ExpiredExecutionCancellation? Receipt = null);

public interface IExpiredCancellationStore
{
    Task<ExpiredClosureResult> CloseExpiredCancellationAsync(ExpiredCancellation request,
        Guid actorUserId, CancellationToken actionCt);
}

public sealed class CloseExpiredCancellationService(IExpiredCancellationStore store)
{
    public Task<ExpiredClosureResult> CloseExpiredCancellationAsync(ExpiredCancellation request,
        Guid actorUserId, CancellationToken actionCt)
    {
        actionCt.ThrowIfCancellationRequested();
        return Valid(request, actorUserId) ? store.CloseExpiredCancellationAsync(request, actorUserId, actionCt)
            : Task.FromResult(new ExpiredClosureResult(new(JobTerminationState.Conflict, JobTerminationFailure.InvalidRequest)));
    }

    public static bool Valid(ExpiredCancellation? r, Guid actor)
    {
        if (r is null || !ObserveForClosureService.Valid(r.Identity, actor)) return false;
        try
        {
            // Only structural validation. The store obtains actual completion/guard times from PostgreSQL.
            var end = r.ExpectedLeaseExpiresAtUtc > r.ExpectedCancellationRequestedAtUtc
                ? r.ExpectedLeaseExpiresAtUtc : r.ExpectedCancellationRequestedAtUtc;
            _ = ExpiredExecutionCancellation.Create(r.OperationId, r.Identity.JobId, r.Identity.RequirementId,
                r.Identity.ExecutionRequestId, r.Identity.ClaimOperationId, actor, r.Identity.JobId,
                r.LeaseOwner, r.LeaseGeneration, r.AssignedAtUtc, r.ExpectedLeaseExpiresAtUtc,
                r.ExpectedProcessingAttemptId, r.ExpectedCancellationRequestedAtUtc, end, end, 1);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
