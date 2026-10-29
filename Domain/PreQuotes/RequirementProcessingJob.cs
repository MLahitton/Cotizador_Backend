namespace Domain.PreQuotes;

public enum RequirementProcessingJobState { Queued = 1, Running, Succeeded, Failed, Cancelled }
public enum RequirementProcessingJobTrigger { PublicPreparation = 1, ManualInitial, ManualRetry }

/// <summary>Local lifecycle only. Does not claim work or mutate a Requirement/attempt.</summary>
public sealed class RequirementProcessingJob
{
    public const string PublicationV1 = "sng-job-publication-v1";
    public const string PublicationV2 = "sng-job-publication-v2";
    private RequirementProcessingJob() { }

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid? SubmissionId { get; private set; }
    public Guid ExecutionRequestId { get; private set; }
    public Guid? RetryOfJobId { get; private set; }
    public RequirementProcessingJobState State { get; private set; }
    public RequirementProcessingJobTrigger TriggerKind { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? ProcessingAttemptId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public long LeaseGeneration { get; private set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? PublicationContractVersion { get; private set; }
    public string? PublicationContentHash { get; private set; }
    public string RequiredPublicationVersion { get; private set; } = PublicationV1;
    public int? AutomaticAuthorizationPolicyVersion { get; private set; }
    public Guid? AuthorizedActorUserId { get; private set; }
    public DateTimeOffset? AuthorizedAtUtc { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAtUtc { get; private set; }
    public DateTimeOffset? AuthorizationRevokedAtUtc { get; private set; }
    public DateTimeOffset? DispatchAdmittedAtUtc { get; private set; }
    public Guid? DispatchInstanceId { get; private set; }
    public Guid? DispatchClaimOperationId { get; private set; }

    public static RequirementProcessingJob CreateForAutomaticExperience(Guid jobId, Guid requirementId,
        Guid submissionId, Guid executionRequestId, Guid correlationId, Guid authorizedActorUserId,
        int policyVersion, DateTimeOffset authorizedAtUtc, DateTimeOffset authorizationExpiresAtUtc)
    {
        RequireId(jobId, nameof(jobId));
        RequireId(requirementId, nameof(requirementId));
        RequireId(submissionId, nameof(submissionId));
        RequireId(executionRequestId, nameof(executionRequestId));
        RequireId(correlationId, nameof(correlationId));
        RequireId(authorizedActorUserId, nameof(authorizedActorUserId));
        if (policyVersion != 1) throw new ArgumentException("Unsupported authorization policy.", nameof(policyVersion));
        RequireAuthorizationDate(authorizedAtUtc, nameof(authorizedAtUtc));
        RequireAuthorizationDate(authorizationExpiresAtUtc, nameof(authorizationExpiresAtUtc));
        if (authorizationExpiresAtUtc != authorizedAtUtc.AddHours(24))
            throw new ArgumentException("Authorization must last exactly 24 hours.", nameof(authorizationExpiresAtUtc));
        return new()
        {
            Id = jobId, RequirementId = requirementId, SubmissionId = submissionId,
            ExecutionRequestId = executionRequestId, CorrelationId = correlationId,
            TriggerKind = RequirementProcessingJobTrigger.PublicPreparation,
            RequiredPublicationVersion = PublicationV2, State = RequirementProcessingJobState.Queued,
            CreatedAtUtc = authorizedAtUtc, AutomaticAuthorizationPolicyVersion = policyVersion,
            AuthorizedActorUserId = authorizedActorUserId, AuthorizedAtUtc = authorizedAtUtc,
            AuthorizationExpiresAtUtc = authorizationExpiresAtUtc
        };
    }

    public void AdmitAutomaticDispatch(Guid instanceId, Guid claimOperationId, DateTimeOffset admittedAtUtc)
    {
        RequireAutomaticAuthorization();
        RequireId(instanceId, nameof(instanceId));
        RequireId(claimOperationId, nameof(claimOperationId));
        RequireAuthorizationDate(admittedAtUtc, nameof(admittedAtUtc));
        if (DispatchAdmittedAtUtc.HasValue || AuthorizationRevokedAtUtc.HasValue)
            throw new InvalidOperationException("Authorization has already been admitted or revoked.");
        if (admittedAtUtc < AuthorizedAtUtc || admittedAtUtc >= AuthorizationExpiresAtUtc)
            throw new ArgumentException("Admission must be within the authorization interval.", nameof(admittedAtUtc));
        DispatchAdmittedAtUtc = admittedAtUtc;
        DispatchInstanceId = instanceId;
        DispatchClaimOperationId = claimOperationId;
    }

    public void RevokeAutomaticAuthorization(DateTimeOffset revokedAtUtc)
    {
        RequireAutomaticAuthorization();
        RequireAuthorizationDate(revokedAtUtc, nameof(revokedAtUtc));
        if (revokedAtUtc < AuthorizedAtUtc || revokedAtUtc < DispatchAdmittedAtUtc)
            throw new ArgumentException("Revocation cannot precede issuance or admission.", nameof(revokedAtUtc));
        AuthorizationRevokedAtUtc ??= revokedAtUtc;
    }

    private void RequireAutomaticAuthorization()
    {
        if (!AutomaticAuthorizationPolicyVersion.HasValue)
            throw new InvalidOperationException("This job has no automatic authorization.");
    }

    private static void RequireAuthorizationDate(DateTimeOffset value, string name)
    {
        Requirement.EnsureUtc(value, name);
        if (value.Ticks % 10 != 0) throw new ArgumentException("Exact microseconds are required.", name);
    }

    public static RequirementProcessingJob CreateForVerifiedPublication(Guid requirementId, Guid executionRequestId,
        Guid correlationId, RequirementProcessingJobTrigger triggerKind, DateTimeOffset createdAtUtc,
        Guid? submissionId = null, Guid? requestedByUserId = null, Guid? retryOfJobId = null)
    {
        var job = Create(requirementId, executionRequestId, correlationId, triggerKind, createdAtUtc,
            submissionId, requestedByUserId, retryOfJobId);
        job.RequiredPublicationVersion = PublicationV2;
        return job;
    }

    public static RequirementProcessingJob Create(Guid requirementId, Guid executionRequestId,
        Guid correlationId, RequirementProcessingJobTrigger triggerKind, DateTimeOffset createdAtUtc,
        Guid? submissionId = null, Guid? requestedByUserId = null, Guid? retryOfJobId = null)
    {
        RequireId(requirementId, nameof(requirementId));
        RequireId(executionRequestId, nameof(executionRequestId));
        RequireId(correlationId, nameof(correlationId));
        if (submissionId.HasValue) RequireId(submissionId.Value, nameof(submissionId));
        if (requestedByUserId.HasValue) RequireId(requestedByUserId.Value, nameof(requestedByUserId));
        if (retryOfJobId.HasValue) RequireId(retryOfJobId.Value, nameof(retryOfJobId));
        if (!Enum.IsDefined(triggerKind)
            || (triggerKind == RequirementProcessingJobTrigger.ManualRetry) != retryOfJobId.HasValue)
            throw new ArgumentException("Retry origin requires exactly one previous job reference.", nameof(triggerKind));
        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new()
        {
            Id = Guid.NewGuid(), RequirementId = requirementId, ExecutionRequestId = executionRequestId,
            CorrelationId = correlationId, TriggerKind = triggerKind, CreatedAtUtc = createdAtUtc,
            SubmissionId = submissionId, RequestedByUserId = requestedByUserId, RetryOfJobId = retryOfJobId,
            State = RequirementProcessingJobState.Queued
        };
    }

    public void AssignExecution(string leaseOwner, DateTimeOffset assignedAtUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        RequireState(RequirementProcessingJobState.Queued);
        ValidateDate(assignedAtUtc);
        Requirement.EnsureUtc(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 200)
            throw new ArgumentException("A lease owner of at most 200 characters is required.", nameof(leaseOwner));
        if (leaseExpiresAtUtc <= assignedAtUtc)
            throw new ArgumentException("The lease must expire after assignment.", nameof(leaseExpiresAtUtc));
        State = RequirementProcessingJobState.Running;
        StartedAtUtc = assignedAtUtc;
        LeaseOwner = leaseOwner;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        LeaseGeneration = 1;
    }

    public void LinkStartedAttempt(Guid processingAttemptId)
    {
        RequireState(RequirementProcessingJobState.Running);
        RequireNoCancellation();
        RequireId(processingAttemptId, nameof(processingAttemptId));
        if (ProcessingAttemptId.HasValue && ProcessingAttemptId != processingAttemptId)
            throw new InvalidOperationException("A job can link only one processing attempt.");
        ProcessingAttemptId = processingAttemptId;
        AttemptCount = 1;
    }

    public void RequestCancellation(DateTimeOffset requestedAtUtc)
    {
        ValidateDate(requestedAtUtc);
        if (State == RequirementProcessingJobState.Cancelled) return;
        if (State is not (RequirementProcessingJobState.Queued or RequirementProcessingJobState.Running))
            throw new InvalidOperationException("A completed job cannot be cancelled.");
        CancellationRequestedAtUtc ??= requestedAtUtc;
        if (State == RequirementProcessingJobState.Queued)
            Finish(RequirementProcessingJobState.Cancelled, requestedAtUtc);
    }

    public void ConfirmCancellation(DateTimeOffset finishedAtUtc)
    {
        RequireState(RequirementProcessingJobState.Running);
        if (!CancellationRequestedAtUtc.HasValue)
            throw new InvalidOperationException("Cancellation must be requested first.");
        Finish(RequirementProcessingJobState.Cancelled, finishedAtUtc);
    }

    public void Succeed(DateTimeOffset finishedAtUtc)
    {
        if (RequiredPublicationVersion == PublicationV2)
            throw new InvalidOperationException("Verified publication requires its receipt.");
        CompleteSuccess(finishedAtUtc);
    }

    private void CompleteSuccess(DateTimeOffset finishedAtUtc)
    {
        RequireState(RequirementProcessingJobState.Running);
        RequireNoCancellation();
        if (!ProcessingAttemptId.HasValue || AttemptCount != 1)
            throw new InvalidOperationException("Success requires a linked attempt.");
        Finish(RequirementProcessingJobState.Succeeded, finishedAtUtc);
    }

    public void SucceedWithPublicationReceipt(string contractVersion, string contentHash, DateTimeOffset finishedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(contractVersion) || contractVersion.Length > 100)
            throw new ArgumentException("A publication contract version is required.", nameof(contractVersion));
        if (contentHash is null || contentHash.Length != 64
            || contentHash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("An uppercase SHA-256 digest is required.", nameof(contentHash));
        RequireState(RequirementProcessingJobState.Running);
        RequireNoCancellation();
        if (!ProcessingAttemptId.HasValue || AttemptCount != 1)
            throw new InvalidOperationException("Success requires a linked attempt.");
        if (contractVersion != RequiredPublicationVersion)
            throw new ArgumentException("Publication version differs from the job requirement.", nameof(contractVersion));
        CompleteSuccess(finishedAtUtc);
        PublicationContractVersion = contractVersion;
        PublicationContentHash = contentHash;
    }

    public void Fail(string errorCode, DateTimeOffset finishedAtUtc)
    {
        RequireState(RequirementProcessingJobState.Running);
        RequireNoCancellation();
        // Only safe symbolic codes, never exception/provider messages.
        if (string.IsNullOrEmpty(errorCode) || errorCode.Length > 64
            || errorCode[0] is < 'A' or > 'Z'
            || errorCode.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("A controlled uppercase error code is required.", nameof(errorCode));
        ValidateDate(finishedAtUtc);
        LastErrorCode = errorCode;
        Finish(RequirementProcessingJobState.Failed, finishedAtUtc);
    }

    private void Finish(RequirementProcessingJobState state, DateTimeOffset atUtc)
    {
        ValidateDate(atUtc);
        State = state;
        FinishedAtUtc = atUtc;
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void ValidateDate(DateTimeOffset atUtc)
    {
        Requirement.EnsureUtc(atUtc, nameof(atUtc));
        if (atUtc < CreatedAtUtc || atUtc < StartedAtUtc || atUtc < CancellationRequestedAtUtc)
            throw new ArgumentException("Date precedes the job lifecycle.", nameof(atUtc));
    }

    private void RequireState(RequirementProcessingJobState expected)
    {
        if (State != expected) throw new InvalidOperationException("Invalid job transition.");
    }

    private void RequireNoCancellation()
    {
        if (CancellationRequestedAtUtc.HasValue)
            throw new InvalidOperationException("Cancellation has been requested.");
    }

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("A nonempty identity is required.", name);
    }
}
