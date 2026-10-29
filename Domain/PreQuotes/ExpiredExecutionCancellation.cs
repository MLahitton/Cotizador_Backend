namespace Domain.PreQuotes;

// Structural receipt only; creation does not close a job or establish authority or commit.
public sealed class ExpiredExecutionCancellation
{
    private ExpiredExecutionCancellation() { }
    public Guid OperationId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid ExecutionRequestId { get; private set; }
    public Guid ClaimOperationId { get; private set; }
    public Guid ClosedByActorUserId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string LeaseOwner { get; private set; } = null!;
    public long LeaseGeneration { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public DateTimeOffset ExpectedLeaseExpiresAtUtc { get; private set; }
    public Guid? ExpectedProcessingAttemptId { get; private set; }
    public DateTimeOffset CancellationRequestedAtUtc { get; private set; }
    public DateTimeOffset FinishedAtUtc { get; private set; }
    public DateTimeOffset GuardAtUtc { get; private set; }
    public short PolicyVersion { get; private set; }

    public static ExpiredExecutionCancellation Create(Guid operationId, Guid jobId, Guid requirementId,
        Guid executionRequestId, Guid claimOperationId, Guid closedByActorUserId, Guid correlationId,
        string leaseOwner, long leaseGeneration, DateTimeOffset assignedAtUtc,
        DateTimeOffset expectedLeaseExpiresAtUtc, Guid? expectedProcessingAttemptId,
        DateTimeOffset cancellationRequestedAtUtc, DateTimeOffset finishedAtUtc,
        DateTimeOffset guardAtUtc, short policyVersion)
    {
        foreach (var id in new[] { operationId, jobId, requirementId, executionRequestId,
            claimOperationId, closedByActorUserId, correlationId })
            if (id == Guid.Empty) throw new ArgumentException("A nonempty identity is required.");
        if (expectedProcessingAttemptId == Guid.Empty)
            throw new ArgumentException("A present attempt must be nonempty.", nameof(expectedProcessingAttemptId));
        if (leaseGeneration != 1 || policyVersion != 1)
            throw new ArgumentException("Only generation 1 and cancellation policy 1 are supported.");
        if (leaseOwner is null || leaseOwner.Length != 78 || !leaseOwner.StartsWith("exec:", StringComparison.Ordinal)
            || leaseOwner[41] != ':' || !Guid.TryParseExact(leaseOwner.Substring(5, 36), "D", out var instance)
            || instance == Guid.Empty || leaseOwner != $"exec:{instance:D}:{claimOperationId:D}")
            throw new ArgumentException("The canonical owner must identify this claim and a nonempty instance.", nameof(leaseOwner));
        Date(assignedAtUtc); Date(expectedLeaseExpiresAtUtc); Date(cancellationRequestedAtUtc);
        Date(finishedAtUtc); Date(guardAtUtc);
        if (assignedAtUtc >= expectedLeaseExpiresAtUtc || expectedLeaseExpiresAtUtc > finishedAtUtc
            || finishedAtUtc > guardAtUtc || cancellationRequestedAtUtc > finishedAtUtc)
            throw new ArgumentException("Invalid cancellation receipt timestamps.");
        return new()
        {
            OperationId = operationId, JobId = jobId, RequirementId = requirementId,
            ExecutionRequestId = executionRequestId, ClaimOperationId = claimOperationId,
            ClosedByActorUserId = closedByActorUserId, CorrelationId = correlationId,
            LeaseOwner = leaseOwner, LeaseGeneration = leaseGeneration, AssignedAtUtc = assignedAtUtc,
            ExpectedLeaseExpiresAtUtc = expectedLeaseExpiresAtUtc, ExpectedProcessingAttemptId = expectedProcessingAttemptId,
            CancellationRequestedAtUtc = cancellationRequestedAtUtc, FinishedAtUtc = finishedAtUtc,
            GuardAtUtc = guardAtUtc, PolicyVersion = policyVersion
        };
    }

    private static void Date(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0)
            throw new ArgumentException("UTC with exact microsecond precision is required.");
    }
}
