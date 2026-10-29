using Domain.PreQuotes;
using Domain.PublicExperience;
using Application.PublicExperience;

namespace Infrastructure.Persistence.Repositories;

public sealed partial class PublicExperienceRequirementPreparationRepository
{
    private static bool MatchingEmissionDocuments(IReadOnlyList<RequirementFile> files, IReadOnlyList<PublicPreparationFile> sources)
    {
        if (files.Count == 0 || files.Count != sources.Count) return false;
        for (var i = 0; i < files.Count; i++)
            if (!MatchesSource(files[i], sources[i]) || files[i].StoredSha256 != sources[i].ContentHash.ToUpperInvariant()
                || files[i].StoredLengthBytes != sources[i].SizeBytes || files[i].StoredEvidenceVersion != 1
                || files[i].StoredEvidenceAtUtc is null || string.IsNullOrWhiteSpace(files[i].StorageKey)) return false;
        return true;
    }

    private static bool ValidEmission(RequirementProcessingJob job, PublicExperienceSubmission submission,
        Requirement requirement, Guid owner)
    {
        if (job.SubmissionId != submission.Id || job.RequirementId != requirement.Id
            || job.AuthorizedActorUserId != owner || job.AutomaticAuthorizationPolicyVersion != 1
            || job.AuthorizedAtUtc is null || job.AuthorizationExpiresAtUtc is null
            || job.CreatedAtUtc != job.AuthorizedAtUtc || job.RequestedByUserId is not null || job.RetryOfJobId is not null
            || job.TriggerKind != RequirementProcessingJobTrigger.PublicPreparation
            || job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2) return false;
        try
        {
            var shape = RequirementProcessingJob.CreateForAutomaticExperience(job.Id, requirement.Id, submission.Id,
                job.ExecutionRequestId, job.CorrelationId, owner, 1, job.AuthorizedAtUtc.Value, job.AuthorizationExpiresAtUtc.Value);
            if (job.DispatchAdmittedAtUtc is { } admitted)
                shape.AdmitAutomaticDispatch(job.DispatchInstanceId ?? Guid.Empty, job.DispatchClaimOperationId ?? Guid.Empty, admitted);
            else if (job.DispatchInstanceId is not null || job.DispatchClaimOperationId is not null) return false;
            if (job.AuthorizationRevokedAtUtc is { } revoked) shape.RevokeAutomaticAuthorization(revoked);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Operational fields may evolve after commit; emission identity and time cannot.
    private static bool SameEmission(RequirementProcessingJob expected, RequirementProcessingJob actual) =>
        expected.Id == actual.Id && expected.RequirementId == actual.RequirementId && expected.SubmissionId == actual.SubmissionId
        && expected.ExecutionRequestId == actual.ExecutionRequestId && expected.CorrelationId == actual.CorrelationId
        && expected.TriggerKind == actual.TriggerKind && expected.RequiredPublicationVersion == actual.RequiredPublicationVersion
        && expected.RequestedByUserId == actual.RequestedByUserId && expected.RetryOfJobId == actual.RetryOfJobId
        && expected.CreatedAtUtc == actual.CreatedAtUtc && expected.AutomaticAuthorizationPolicyVersion == actual.AutomaticAuthorizationPolicyVersion
        && expected.AuthorizedActorUserId == actual.AuthorizedActorUserId && expected.AuthorizedAtUtc == actual.AuthorizedAtUtc
        && expected.AuthorizationExpiresAtUtc == actual.AuthorizationExpiresAtUtc;
}
