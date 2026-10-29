namespace Domain.PreQuotes;

public enum JobLeaseOperationKind { Claim = 1, Renew }

// Structural receipt only. Construction neither mutates a job nor proves authority or commit.
public sealed class JobLeaseOperation
{
    private JobLeaseOperation() { }
    public Guid OperationId { get; private set; }
    public JobLeaseOperationKind Kind { get; private set; }
    public Guid JobId { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid ExecutionRequestId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid ClaimOperationId { get; private set; }
    public string LeaseOwner { get; private set; } = null!;
    public long LeaseGeneration { get; private set; }
    public short PolicyVersion { get; private set; }
    public DateTimeOffset? ExpectedLeaseExpiresAtUtc { get; private set; }
    public Guid? ExpectedProcessingAttemptId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public DateTimeOffset AppliedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool IsEffectiveExtension => Kind == JobLeaseOperationKind.Renew
        && ExpiresAtUtc > ExpectedLeaseExpiresAtUtc;

    public static JobLeaseOperation CreateClaim(Guid operationId, Guid jobId, Guid requirementId,
        Guid executionRequestId, Guid actorUserId, Guid claimOperationId, string leaseOwner,
        long leaseGeneration, short policyVersion, DateTimeOffset assignedAtUtc,
        DateTimeOffset appliedAtUtc, DateTimeOffset expiresAtUtc) =>
        Create(operationId, JobLeaseOperationKind.Claim, jobId, requirementId, executionRequestId,
            actorUserId, claimOperationId, leaseOwner, leaseGeneration, policyVersion, null, null,
            assignedAtUtc, appliedAtUtc, expiresAtUtc);

    public static JobLeaseOperation CreateRenew(Guid operationId, Guid jobId, Guid requirementId,
        Guid executionRequestId, Guid actorUserId, Guid claimOperationId, string leaseOwner,
        long leaseGeneration, short policyVersion, DateTimeOffset expectedLeaseExpiresAtUtc,
        Guid? expectedProcessingAttemptId, DateTimeOffset assignedAtUtc, DateTimeOffset appliedAtUtc,
        DateTimeOffset expiresAtUtc) =>
        Create(operationId, JobLeaseOperationKind.Renew, jobId, requirementId, executionRequestId,
            actorUserId, claimOperationId, leaseOwner, leaseGeneration, policyVersion,
            expectedLeaseExpiresAtUtc, expectedProcessingAttemptId, assignedAtUtc, appliedAtUtc, expiresAtUtc);

    private static JobLeaseOperation Create(Guid operationId, JobLeaseOperationKind kind, Guid jobId,
        Guid requirementId, Guid executionRequestId, Guid actorUserId, Guid claimOperationId,
        string leaseOwner, long leaseGeneration, short policyVersion, DateTimeOffset? expected,
        Guid? attempt, DateTimeOffset assigned, DateTimeOffset applied, DateTimeOffset expires)
    {
        foreach (var id in new[] { operationId, jobId, requirementId, executionRequestId, actorUserId, claimOperationId })
            if (id == Guid.Empty) throw new ArgumentException("A nonempty identity is required.");
        if (attempt == Guid.Empty) throw new ArgumentException("A present attempt must be nonempty.");
        if (leaseGeneration != 1 || policyVersion != 1)
            throw new ArgumentException("Only generation 1 and lease policy 1 are supported.");
        if (leaseOwner is null || leaseOwner.Length != 78 || !leaseOwner.StartsWith("exec:", StringComparison.Ordinal)
            || leaseOwner[41] != ':' || !Guid.TryParseExact(leaseOwner.Substring(5, 36), "D", out var instance)
            || instance == Guid.Empty || leaseOwner != $"exec:{instance:D}:{claimOperationId:D}")
            throw new ArgumentException("The canonical owner must identify this claim and a nonempty instance.", nameof(leaseOwner));
        Date(assigned); Date(applied); Date(expires);
        if (expected.HasValue) Date(expected.Value);
        // AddSeconds rejects overflow before any object is materialized.
        var horizon = applied.AddSeconds(120);
        if (kind == JobLeaseOperationKind.Claim)
        {
            if (operationId != claimOperationId || assigned != applied || expires != horizon)
                throw new ArgumentException("Invalid claim identity or timestamps.");
        }
        else if (operationId == claimOperationId || !expected.HasValue || applied < assigned
            || expected.Value <= applied || expires != (expected.Value > horizon ? expected.Value : horizon))
            throw new ArgumentException("Invalid renewal identity or timestamps.");
        if (expires <= assigned) throw new ArgumentException("Expiration must follow assignment.");
        return new()
        {
            OperationId = operationId, Kind = kind, JobId = jobId, RequirementId = requirementId,
            ExecutionRequestId = executionRequestId, ActorUserId = actorUserId, ClaimOperationId = claimOperationId,
            LeaseOwner = leaseOwner, LeaseGeneration = leaseGeneration, PolicyVersion = policyVersion,
            ExpectedLeaseExpiresAtUtc = expected, ExpectedProcessingAttemptId = attempt,
            AssignedAtUtc = assigned, AppliedAtUtc = applied, ExpiresAtUtc = expires
        };
    }

    private static void Date(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0)
            throw new ArgumentException("UTC with exact microsecond precision is required.");
    }
}
