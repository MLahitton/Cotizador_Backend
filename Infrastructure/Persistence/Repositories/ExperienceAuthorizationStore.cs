using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class ExperienceAuthorizationStore(DbContextOptions<ApplicationDbContext> options) : IExperienceAuthorizationStore
{
    private sealed class WriteState
    {
        internal bool MutationStarted;
        internal bool CommitAttempted;
        internal bool RolledBack;
    }
    private sealed record Result(ExperienceAuthorizationFailure Failure = ExperienceAuthorizationFailure.None,
        bool Existing = false, ConfirmedExperienceAdmission? Admission = null, DateTimeOffset? Revoked = null);

    public async Task<ExperienceAdmissionWriteResult> AdmitAsync(ExperienceAdmissionCandidate candidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (candidate is null || candidate.JobId == Guid.Empty || candidate.InstanceId == Guid.Empty || candidate.ClaimOperationId == Guid.Empty)
            return new(ExperienceAdmissionState.Rejected, ExperienceAuthorizationFailure.InvalidRequest);
        var write = new WriteState();
        try
        {
            var result = await RunAsync(candidate, null, write, ct);
            if (result.Failure != ExperienceAuthorizationFailure.None) return new(ExperienceAdmissionState.Rejected, result.Failure);
            return result.Existing ? new(ExperienceAdmissionState.AlreadyAdmitted)
                : new(ExperienceAdmissionState.Admitted, Confirmed: result.Admission);
        }
        catch (Exception)
        {
            return new(write.CommitAttempted || (write.MutationStarted && !write.RolledBack)
                ? ExperienceAdmissionState.Indeterminate : ExperienceAdmissionState.PersistenceError);
        }
        finally { ct.ThrowIfCancellationRequested(); }
    }

    public async Task<ExperienceRevocationResult> RevokeAsync(Guid jobId, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (jobId == Guid.Empty || actorUserId == Guid.Empty)
            return new(ExperienceRevocationState.Rejected, ExperienceAuthorizationFailure.InvalidRequest);
        var write = new WriteState();
        try
        {
            var result = await RunAsync(new(jobId, Guid.Empty, Guid.Empty), actorUserId, write, ct);
            if (result.Failure != ExperienceAuthorizationFailure.None) return new(ExperienceRevocationState.Rejected, result.Failure);
            return new(result.Existing ? ExperienceRevocationState.AlreadyRevoked : ExperienceRevocationState.Revoked,
                RevokedAtUtc: result.Revoked);
        }
        catch (Exception)
        {
            return new(write.CommitAttempted || (write.MutationStarted && !write.RolledBack)
                ? ExperienceRevocationState.Indeterminate : ExperienceRevocationState.PersistenceError);
        }
        finally { ct.ThrowIfCancellationRequested(); }
    }

    private async Task<Result> RunAsync(ExperienceAdmissionCandidate candidate, Guid? revokingActor, WriteState write, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var route = await (from j in db.RequirementProcessingJobs.AsNoTracking()
                join r in db.Requirements.AsNoTracking() on j.RequirementId equals r.Id
                join q in db.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
                join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
                where j.Id == candidate.JobId
                select new { j.SubmissionId, j.AuthorizedActorUserId, RequirementId = r.Id, QuoteId = q.Id,
                    ProjectId = p.Id, p.ClientId }).SingleOrDefaultAsync(ct);
            if (route is null) return new(ExperienceAuthorizationFailure.NotFound);
            var submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
                $"SELECT * FROM core.public_experience_submissions WHERE id = {route.SubmissionId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var actor = revokingActor ?? route.AuthorizedActorUserId ?? Guid.Empty;
            var user = await db.Users.FromSqlInterpolated(
                $"SELECT * FROM identity.users WHERE id = {actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (user is null) return new(ExperienceAuthorizationFailure.NotFound);
            if (!user.IsActive) return new(ExperienceAuthorizationFailure.InactiveUser);
            var client = await db.Clients.FromSqlInterpolated(
                $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var project = await db.Projects.FromSqlInterpolated(
                $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.CreatedByUserId != actor) return new(ExperienceAuthorizationFailure.NotFound);
            var quote = await db.PreQuotes.FromSqlInterpolated(
                $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var requirement = await db.Requirements.FromSqlInterpolated(
                $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var jobs = await db.RequirementProcessingJobs.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_jobs WHERE requirement_id = {route.RequirementId} ORDER BY id FOR UPDATE").AsNoTracking().ToListAsync(ct);
            var job = jobs.SingleOrDefault(x => x.Id == candidate.JobId);
            if (client is null || quote is null || requirement is null || job is null || submission is null
                || project.ClientId != client.Id || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id
                || job.SubmissionId != submission.Id || submission.ClientId != client.Id || submission.ProjectId != project.Id
                || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id)
                return new(ExperienceAuthorizationFailure.IdentityConflict);
            if (!Metadata(job) || job.AuthorizedActorUserId != route.AuthorizedActorUserId)
                return new(ExperienceAuthorizationFailure.MetadataConflict);
            if (revokingActor.HasValue)
            {
                if (job.AuthorizationRevokedAtUtc.HasValue) return new(Existing: true, Revoked: job.AuthorizationRevokedAtUtc);
                write.MutationStarted = true;
                var rows = await db.RequirementProcessingJobs.FromSqlInterpolated($"""
                    WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS t)
                    UPDATE core.requirement_processing_jobs j SET authorization_revoked_at_utc = clock.t
                    FROM clock WHERE j.id = {job.Id} AND j.authorization_revoked_at_utc IS NULL
                      AND j.automatic_authorization_policy_version = 1
                      AND j.authorized_actor_user_id = {job.AuthorizedActorUserId}
                      AND j.authorized_at_utc = {job.AuthorizedAtUtc} AND j.authorization_expires_at_utc = {job.AuthorizationExpiresAtUtc}
                      AND clock.t >= j.authorized_at_utc AND (j.dispatch_admitted_at_utc IS NULL OR clock.t >= j.dispatch_admitted_at_utc)
                    RETURNING j.*
                    """).AsNoTracking().ToListAsync(ct);
                if (rows.Count != 1) return new(ExperienceAuthorizationFailure.NotYetValid);
                write.CommitAttempted = true;
                await tx.CommitAsync(ct);
                return new(Revoked: rows[0].AuthorizationRevokedAtUtc);
            }
            if (!client.IsActive || !project.IsActive) return new(ExperienceAuthorizationFailure.InactiveContext);
            if (job.AuthorizationRevokedAtUtc.HasValue) return new(ExperienceAuthorizationFailure.Revoked);
            if (job.DispatchAdmittedAtUtc.HasValue) return new(Existing: true);
            if (job.State != RequirementProcessingJobState.Queued || job.CancellationRequestedAtUtc.HasValue
                || job.LeaseOwner is not null || job.LeaseExpiresAtUtc.HasValue || job.LeaseGeneration != 0
                || job.StartedAtUtc.HasValue || job.FinishedAtUtc.HasValue || job.LastErrorCode is not null
                || job.PublicationContractVersion is not null || job.PublicationContentHash is not null
                || !requirement.IsCurrent || requirement.Status != RequirementStatus.Pending
                || requirement.CommercialLine is null || !Enum.IsDefined(requirement.CommercialLine.Value))
                return new(ExperienceAuthorizationFailure.StateConflict);
            var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirement.Id} ORDER BY id FOR UPDATE").AsNoTracking().ToListAsync(ct);
            if (jobs.Count != 1 || attempts.Count != 0 || job.AttemptCount != 0 || job.ProcessingAttemptId.HasValue
                || await db.JobLeaseOperations.AnyAsync(x => x.JobId == job.Id, ct)
                || await db.ExpiredExecutionCancellations.AnyAsync(x => x.JobId == job.Id, ct)
                || await db.RequirementTechnicalProposals.AnyAsync(x => x.RequirementId == requirement.Id, ct))
                return new(ExperienceAuthorizationFailure.HistoryConflict);
            var files = await db.RequirementFiles.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_files WHERE requirement_id = {requirement.Id} ORDER BY id FOR SHARE").AsNoTracking().ToListAsync(ct);
            var sources = await db.Set<PublicExperienceSubmissionFile>().FromSqlInterpolated(
                $"SELECT * FROM core.public_experience_submission_files WHERE submission_id = {submission.Id} ORDER BY id FOR SHARE").AsNoTracking().ToListAsync(ct);
            if (submission.Status != PublicExperienceSubmissionStatuses.Processed || submission.RequirementPreparedAtUtc is null
                || submission.RequirementPreparationErrorCode is not null || quote.CreatedByUserId != actor
                || requirement.CreatedByUserId != actor || !Documents(files, sources))
                return new(ExperienceAuthorizationFailure.DocumentsConflict);
            var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
            if (now < job.AuthorizedAtUtc) return new(ExperienceAuthorizationFailure.NotYetValid);
            if (now >= job.AuthorizationExpiresAtUtc) return new(ExperienceAuthorizationFailure.Expired);
            write.MutationStarted = true;
            var admitted = await db.RequirementProcessingJobs.FromSqlInterpolated($"""
                WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS t)
                UPDATE core.requirement_processing_jobs j
                SET dispatch_admitted_at_utc = clock.t, dispatch_instance_id = {candidate.InstanceId}, dispatch_claim_operation_id = {candidate.ClaimOperationId}
                FROM clock WHERE j.id = {job.Id} AND j.requirement_id = {requirement.Id} AND j.submission_id = {submission.Id}
                  AND j.execution_request_id = {job.ExecutionRequestId} AND j.correlation_id = {job.CorrelationId}
                  AND j.trigger_kind = 'PublicPreparation' AND j.required_publication_version = {RequirementProcessingJob.PublicationV2}
                  AND j.automatic_authorization_policy_version = 1 AND j.authorized_actor_user_id = {actor}
                  AND j.authorized_at_utc = {job.AuthorizedAtUtc} AND j.authorization_expires_at_utc = {job.AuthorizationExpiresAtUtc}
                  AND clock.t >= j.authorized_at_utc AND clock.t < j.authorization_expires_at_utc
                  AND j.authorization_revoked_at_utc IS NULL AND j.dispatch_admitted_at_utc IS NULL
                  AND j.dispatch_instance_id IS NULL AND j.dispatch_claim_operation_id IS NULL
                  AND j.state = 'Queued' AND j.retry_of_job_id IS NULL AND j.requested_by_user_id IS NULL
                  AND j.cancellation_requested_at_utc IS NULL AND j.lease_owner IS NULL AND j.lease_expires_at_utc IS NULL
                  AND j.lease_generation = 0 AND j.started_at_utc IS NULL AND j.finished_at_utc IS NULL
                  AND j.attempt_count = 0 AND j.processing_attempt_id IS NULL AND j.last_error_code IS NULL
                  AND j.publication_contract_version IS NULL AND j.publication_content_hash IS NULL
                RETURNING j.*
                """).AsNoTracking().ToListAsync(ct);
            if (admitted.Count != 1) return new(ExperienceAuthorizationFailure.TimeConflict);
            var applied = admitted[0];
            var confirmation = new ConfirmedExperienceAdmission(job.Id, requirement.Id, submission.Id, job.ExecutionRequestId,
                job.CorrelationId, actor, candidate.InstanceId, candidate.ClaimOperationId, 1,
                job.AuthorizedAtUtc!.Value, job.AuthorizationExpiresAtUtc!.Value, applied.DispatchAdmittedAtUtc!.Value);
            write.CommitAttempted = true;
            await tx.CommitAsync(ct);
            return new(Admission: confirmation);
        }
        catch
        {
            if (!write.CommitAttempted)
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await tx.RollbackAsync(rollback.Token); write.RolledBack = true; }
                catch (Exception) { /* A failed rollback does not establish absence of effects. */ }
            }
            throw;
        }
    }

    private static bool Metadata(RequirementProcessingJob job)
    {
        if (job.AutomaticAuthorizationPolicyVersion != 1 || job.AuthorizedActorUserId is null
            || job.AuthorizedAtUtc is null || job.AuthorizationExpiresAtUtc is null || job.SubmissionId is null
            || job.TriggerKind != RequirementProcessingJobTrigger.PublicPreparation || job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2
            || job.RequestedByUserId.HasValue || job.RetryOfJobId.HasValue || job.CreatedAtUtc != job.AuthorizedAtUtc) return false;
        try
        {
            var shape = RequirementProcessingJob.CreateForAutomaticExperience(job.Id, job.RequirementId, job.SubmissionId.Value,
                job.ExecutionRequestId, job.CorrelationId, job.AuthorizedActorUserId.Value, 1,
                job.AuthorizedAtUtc.Value, job.AuthorizationExpiresAtUtc.Value);
            if (job.DispatchAdmittedAtUtc.HasValue)
                shape.AdmitAutomaticDispatch(job.DispatchInstanceId ?? Guid.Empty, job.DispatchClaimOperationId ?? Guid.Empty, job.DispatchAdmittedAtUtc.Value);
            else if (job.DispatchInstanceId.HasValue || job.DispatchClaimOperationId.HasValue) return false;
            if (job.AuthorizationRevokedAtUtc.HasValue) shape.RevokeAutomaticAuthorization(job.AuthorizationRevokedAtUtc.Value);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool Documents(List<RequirementFile> files, List<PublicExperienceSubmissionFile> sources)
    {
        if (files.Count == 0 || files.Count != sources.Count || sources.Select(x => x.Sequence).Distinct().Count() != sources.Count) return false;
        var ordered = files.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();
        var originals = sources.OrderBy(x => x.Sequence).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var f = ordered[i]; var s = originals[i];
            if (string.IsNullOrWhiteSpace(s.StorageKey) || string.IsNullOrWhiteSpace(f.StorageKey)
                || s.StoredEvidenceVersion != 1 || s.StoredEvidenceAtUtc is null || s.StoredLengthBytes != s.SizeBytes || s.SizeBytes <= 0
                || s.ContentHash.Length != 64 || s.ContentHash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F'))
                || f.StoredEvidenceVersion != 1 || f.StoredEvidenceAtUtc is null || f.StoredLengthBytes != s.SizeBytes
                || f.StoredSha256 != s.ContentHash || f.SizeBytes != s.SizeBytes
                || f.OriginalFileName != s.OriginalFileName.Trim() || f.ContentType != s.ContentType.Trim()) return false;
        }
        return true;
    }
}
