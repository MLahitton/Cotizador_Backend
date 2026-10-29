using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.PreQuotes.ProcessRequirement;

public enum ExperienceAuthorizationFailure
{
    None, InvalidRequest, NotFound, InactiveUser, InactiveContext, IdentityConflict,
    MetadataConflict, StateConflict, HistoryConflict, DocumentsConflict, Revoked, NotYetValid, Expired, TimeConflict
}
public enum ExperienceAdmissionState { Admitted, AlreadyAdmitted, Rejected, PersistenceError, Indeterminate }
public enum ExperienceRevocationState { Revoked, AlreadyRevoked, Rejected, PersistenceError, Indeterminate }

// Internal composition data, not a public request or a credential.
public sealed record ExperienceAdmissionCandidate(Guid JobId, Guid InstanceId, Guid ClaimOperationId);
public sealed record ConfirmedExperienceAdmission(Guid JobId, Guid RequirementId, Guid SubmissionId,
    Guid ExecutionRequestId, Guid CorrelationId, Guid ActorUserId, Guid InstanceId, Guid ClaimOperationId,
    int PolicyVersion, DateTimeOffset AuthorizedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset AdmittedAtUtc);
public sealed record ExperienceAdmissionWriteResult(ExperienceAdmissionState State,
    ExperienceAuthorizationFailure Failure = ExperienceAuthorizationFailure.None,
    ConfirmedExperienceAdmission? Confirmed = null);
public sealed record ExperienceRevocationResult(ExperienceRevocationState State,
    ExperienceAuthorizationFailure Failure = ExperienceAuthorizationFailure.None, DateTimeOffset? RevokedAtUtc = null);
public sealed record ExperienceAdmissionResult(ExperienceAdmissionState State,
    ExperienceAuthorizationFailure Failure = ExperienceAuthorizationFailure.None, ExperienceExecutionPermit? Permit = null);

[JsonConverter(typeof(ExperienceExecutionPermit.SerializationRejected))]
public sealed class ExperienceExecutionPermit
{
    internal ExperienceExecutionPermit(ConfirmedExperienceAdmission admission) => Admission = admission;
    public ConfirmedExperienceAdmission Admission { get; }

    internal sealed class SerializationRejected : JsonConverter<ExperienceExecutionPermit>
    {
        public SerializationRejected() { }
        public override ExperienceExecutionPermit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Execution permits cannot be bound from JSON.");
        public override void Write(Utf8JsonWriter writer, ExperienceExecutionPermit value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Execution permits are not HTTP contracts.");
    }
}

public interface IExperienceAuthorizationStore
{
    Task<ExperienceAdmissionWriteResult> AdmitAsync(ExperienceAdmissionCandidate candidate, CancellationToken ct);
    Task<ExperienceRevocationResult> RevokeAsync(Guid jobId, Guid actorUserId, CancellationToken ct);
}

public sealed class ExperienceAdmissionService(IExperienceAuthorizationStore store)
{
    public async Task<ExperienceAdmissionResult> AdmitAsync(Guid jobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (jobId == Guid.Empty) return new(ExperienceAdmissionState.Rejected, ExperienceAuthorizationFailure.InvalidRequest);
        var candidate = new ExperienceAdmissionCandidate(jobId, Guid.NewGuid(), Guid.NewGuid());
        var result = await store.AdmitAsync(candidate, ct);
        ct.ThrowIfCancellationRequested();
        if (result.State != ExperienceAdmissionState.Admitted) return new(result.State, result.Failure);
        var confirmed = result.Confirmed;
        if (confirmed is null || confirmed.JobId != jobId || confirmed.InstanceId != candidate.InstanceId
            || confirmed.ClaimOperationId != candidate.ClaimOperationId)
            return new(ExperienceAdmissionState.Indeterminate);
        return new(ExperienceAdmissionState.Admitted, Permit: new(confirmed));
    }
}

public sealed class RevokeExperienceAuthorizationService(IExperienceAuthorizationStore store)
{
    public async Task<ExperienceRevocationResult> RevokeAsync(Guid jobId, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (jobId == Guid.Empty || actorUserId == Guid.Empty)
            return new(ExperienceRevocationState.Rejected, ExperienceAuthorizationFailure.InvalidRequest);
        try { return await store.RevokeAsync(jobId, actorUserId, ct); }
        finally { ct.ThrowIfCancellationRequested(); }
    }
}
