namespace Application.PreQuotes.ProcessRequirement;

public sealed record RenewActiveLease(Guid JobId, Guid RequirementId, Guid ExecutionRequestId,
    Guid OperationId, Guid ClaimOperationId, string LeaseOwner, long LeaseGeneration,
    DateTimeOffset ExpectedLeaseExpiresAtUtc, Guid? ExpectedProcessingAttemptId)
{
    public ExperienceExecutionPermit? Permit { get; init; }
}

public interface IActiveLeaseRenewalStore
{
    Task<LeaseOperationResult> RenewActiveV2Async(RenewActiveLease request, Guid actorUserId, CancellationToken ct);
}

public sealed class RenewActiveLeaseService(IActiveLeaseRenewalStore store)
{
    public async Task<LeaseOperationResult> RenewActiveV2Async(RenewActiveLease request, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = Valid(request, actorUserId)
            ? await store.RenewActiveV2Async(request, actorUserId, ct)
            : new(LeaseOperationState.Rejected, LeaseOperationFailure.InvalidRequest);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public static bool Valid(RenewActiveLease? e, Guid actor) => e is not null && actor != Guid.Empty
        && e.JobId != Guid.Empty && e.RequirementId != Guid.Empty && e.ExecutionRequestId != Guid.Empty
        && e.OperationId != Guid.Empty && e.ClaimOperationId != Guid.Empty && e.OperationId != e.ClaimOperationId
        && e.ExpectedProcessingAttemptId != Guid.Empty && e.LeaseGeneration == 1
        && e.ExpectedLeaseExpiresAtUtc.Offset == TimeSpan.Zero && e.ExpectedLeaseExpiresAtUtc.Ticks % 10 == 0
        && e.LeaseOwner is { Length: 78 } && e.LeaseOwner.StartsWith("exec:", StringComparison.Ordinal)
        && Guid.TryParseExact(e.LeaseOwner.Substring(5, 36), "D", out var instance) && instance != Guid.Empty
        && e.LeaseOwner == $"exec:{instance:D}:{e.ClaimOperationId:D}";
}
