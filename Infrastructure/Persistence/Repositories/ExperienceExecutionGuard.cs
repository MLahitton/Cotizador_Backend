using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

// Call only after the store has locked and authorized the complete route.
// Provenance survives expiry/revocation; admitting a new operation also requires LiveAsync.
internal static class ExperienceExecutionGuard
{
    internal static bool Matches(RequirementProcessingJob job, ExperienceExecutionPermit? permit, Guid actor)
    {
        if (job.AutomaticAuthorizationPolicyVersion is null) return permit is null;
        if (permit is null) return false;
        var a = permit.Admission;
        return job.AutomaticAuthorizationPolicyVersion == 1 && a.PolicyVersion == 1
            && job.RequiredPublicationVersion == RequirementProcessingJob.PublicationV2
            && job.TriggerKind == RequirementProcessingJobTrigger.PublicPreparation
            && job.RetryOfJobId is null && job.RequestedByUserId is null
            && a.JobId == job.Id && a.RequirementId == job.RequirementId && a.SubmissionId == job.SubmissionId
            && a.ExecutionRequestId == job.ExecutionRequestId && a.CorrelationId == job.CorrelationId
            && a.ActorUserId == actor && a.ActorUserId == job.AuthorizedActorUserId
            && a.AuthorizedAtUtc == job.AuthorizedAtUtc && a.AuthorizedAtUtc == job.CreatedAtUtc
            && a.ExpiresAtUtc == job.AuthorizationExpiresAtUtc
            && a.AdmittedAtUtc == job.DispatchAdmittedAtUtc && a.InstanceId == job.DispatchInstanceId
            && a.ClaimOperationId == job.DispatchClaimOperationId;
    }

    internal static bool Assignment(ExperienceExecutionPermit? permit, string owner, long generation) =>
        permit is null || generation == 1
            && owner == $"exec:{permit.Admission.InstanceId:D}:{permit.Admission.ClaimOperationId:D}";

    internal static async Task<bool> LiveAsync(ApplicationDbContext db, RequirementProcessingJob job, CancellationToken ct)
    {
        if (job.AutomaticAuthorizationPolicyVersion is null) return true;
        if (job.AuthorizationRevokedAtUtc.HasValue) return false;
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        return job.AuthorizedAtUtc <= now && now < job.AuthorizationExpiresAtUtc;
    }
}
