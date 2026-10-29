using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

// Backend provenance, not a cryptographic capability. No public constructor or arbitrary Task input.
public sealed class LocalDrainCompletion
{
    internal LocalDrainCompletion(JobLeaseOperation claim)
    {
        Identity = new(claim.JobId, claim.RequirementId, claim.ExecutionRequestId, claim.OperationId);
        LeaseOwner = claim.LeaseOwner;
        LeaseGeneration = claim.LeaseGeneration;
        AssignedAtUtc = claim.AssignedAtUtc;
    }
    public ExecutionClosureIdentity Identity { get; }
    public string LeaseOwner { get; }
    public long LeaseGeneration { get; }
    public DateTimeOffset AssignedAtUtc { get; }
}

// Owns one real supervisor invocation. It does not schedule or confirm cancellation.
public sealed class LocalExecutionInvocation(IInitialLeaseClaimStore claim,
    CoordinateAssignedExecutionService coordinator, ObserveAssignedExecutionService observer,
    RenewActiveLeaseService renew, TimeProvider time)
{
    private int attached;
    private LocalDrainCompletion? drain;
    public LocalDrainCompletion? DrainCompletion => Volatile.Read(ref drain);

    public async Task<AssignedSupervisionResult> ExecuteAsync(ClaimInitialLease request, Guid actor,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref attached, 1) != 0) throw new InvalidOperationException("Invocation already used.");
        var capture = new CapturedClaim(claim);
        var supervisor = new SuperviseAssignedExecutionService(new(capture), coordinator, observer, renew, time);
        try { return await supervisor.ExecuteAsync(request, actor, cancellationToken); }
        finally
        {
            // ExecuteAsync joins coordinator/cycle (including readers/reconciliation) before completing,
            // also on caller OCE. A recognized claim never proves ownership of another invocation.
            if (capture.OwnClaim is { } receipt)
                Volatile.Write(ref drain, new LocalDrainCompletion(receipt));
        }
    }

    private sealed class CapturedClaim(IInitialLeaseClaimStore inner) : IInitialLeaseClaimStore
    {
        public JobLeaseOperation? OwnClaim { get; private set; }
        public async Task<LeaseOperationResult> ClaimInitialV2Async(ClaimInitialLease request, Guid actor, CancellationToken ct)
        {
            var result = await inner.ClaimInitialV2Async(request, actor, ct);
            if (result.State == LeaseOperationState.Claimed && result.Receipt is { } c
                && c.Kind == JobLeaseOperationKind.Claim && c.OperationId == request.OperationId
                && c.ClaimOperationId == request.OperationId && c.JobId == request.JobId
                && c.RequirementId == request.RequirementId && c.ExecutionRequestId == request.ExecutionRequestId
                && c.ActorUserId == actor && c.LeaseGeneration == 1
                && c.LeaseOwner == $"exec:{request.InstanceId:D}:{request.OperationId:D}")
                OwnClaim = c;
            return result;
        }
    }
}
