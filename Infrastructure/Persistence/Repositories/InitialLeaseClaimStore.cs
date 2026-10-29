using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class InitialLeaseClaimStore(DbContextOptions<ApplicationDbContext> options) : IInitialLeaseClaimStore
{
    private sealed class WriteState
    {
        internal bool CommitAttempted;
        internal bool MutationStarted;
        internal bool RolledBack;
        internal JobLeaseOperation? Candidate;
    }

    public async Task<LeaseOperationResult> ClaimInitialV2Async(ClaimInitialLease request, Guid actorUserId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!ClaimInitialLeaseService.Valid(request, actorUserId)) return Reject(LeaseOperationFailure.InvalidRequest);
        var write = new WriteState();
        LeaseOperationResult result;
        try { result = await RunAsync(request, actorUserId, write, false, ct); }
        catch (Exception) when (write.CommitAttempted)
        {
            // The failed scopes are gone. Observation cannot retry mutation or adopt another claim.
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            result = Unknown();
            try
            {
                var found = await RunAsync(request, actorUserId, new(), true, observation.Token);
                if (found.State == LeaseOperationState.AlreadyApplied && write.Candidate is not null
                    && found.Receipt is not null && Equal(write.Candidate, found.Receipt)) result = found;
            }
            catch (Exception) { /* Absence or failure is not proof of rollback. */ }
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (OperationCanceledException) { throw; }
        catch (PostgresException ex) when (write.RolledBack && ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { result = Conflict(LeaseOperationFailure.ReceiptConflict); }
        catch (Exception)
        { result = write.MutationStarted && !write.RolledBack ? Unknown() : new(LeaseOperationState.PersistenceError); }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    private static LeaseOperationResult Reject(LeaseOperationFailure f) => new(LeaseOperationState.Rejected, f);
    private static LeaseOperationResult Conflict(LeaseOperationFailure f) => new(LeaseOperationState.Conflict, f);
    private static LeaseOperationResult Unknown() => new(LeaseOperationState.Indeterminate);

    private async Task<LeaseOperationResult> RunAsync(ClaimInitialLease e, Guid actor, WriteState write, bool observeOnly, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var route = await (from j in db.RequirementProcessingJobs.AsNoTracking()
                join r in db.Requirements.AsNoTracking() on j.RequirementId equals r.Id
                join q in db.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
                join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
                where j.Id == e.JobId
                select new { j.SubmissionId, RequirementId = r.Id, QuoteId = q.Id, ProjectId = p.Id, p.ClientId }).SingleOrDefaultAsync(ct);
            PublicExperienceSubmission? submission = null;
            if (route?.SubmissionId is Guid sid)
                submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
                    $"SELECT * FROM core.public_experience_submissions WHERE id = {sid} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var user = await db.Users.FromSqlInterpolated(
                $"SELECT * FROM identity.users WHERE id = {actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (user is null) return Reject(LeaseOperationFailure.NotFound);
            if (!user.IsActive) return Reject(LeaseOperationFailure.InactiveUser);
            if (route is null) return Reject(LeaseOperationFailure.NotFound);
            var client = await db.Set<Client>().FromSqlInterpolated(
                $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var project = await db.Set<Project>().FromSqlInterpolated(
                $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.CreatedByUserId != actor) return Reject(LeaseOperationFailure.NotFound);
            var quote = await db.PreQuotes.FromSqlInterpolated(
                $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var requirement = await db.Requirements.FromSqlInterpolated(
                $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var job = await db.RequirementProcessingJobs.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_jobs WHERE id = {e.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (client is null || quote is null || requirement is null || job is null
                || project.ClientId != client.Id || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id
                || job.RequirementId != requirement.Id || job.SubmissionId != route.SubmissionId
                || (job.SubmissionId.HasValue && (submission is null || submission.ClientId != client.Id
                    || submission.ProjectId != project.Id || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id)))
                return Conflict(LeaseOperationFailure.IdentityMismatch);
            if (job.RequirementId != e.RequirementId || job.ExecutionRequestId != e.ExecutionRequestId)
                return Conflict(LeaseOperationFailure.IdentityMismatch);
            if (!ExperienceExecutionGuard.Matches(job, e.Permit, actor)
                || (e.Permit is { } permit && (e.InstanceId != permit.Admission.InstanceId
                    || e.OperationId != permit.Admission.ClaimOperationId)))
                return Reject(LeaseOperationFailure.AuthorizationConflict);
            var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirement.Id} ORDER BY id FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            var owner = $"exec:{e.InstanceId:D}:{e.OperationId:D}";
            // Job serializes its receipt writers. Read only after authority and route are established.
            var prior = await db.JobLeaseOperations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == e.OperationId, ct);
            if (prior is not null)
            {
                var linked = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
                if (!Matches(prior, e, actor, owner) || job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2
                    || job.StartedAtUtc != prior.AssignedAtUtc || job.LeaseGeneration != 1
                    || job.State == RequirementProcessingJobState.Queued
                    || (job.State == RequirementProcessingJobState.Running && (job.LeaseOwner != owner || job.LeaseExpiresAtUtc is null)))
                    return Conflict(LeaseOperationFailure.ReceiptConflict);
                if (job.ProcessingAttemptId.HasValue
                    ? job.AttemptCount != 1 || linked is null || linked.CorrelationId != job.CorrelationId
                        || linked.StartedAtUtc is null || linked.StartedAtUtc < job.StartedAtUtc
                        || (job.State == RequirementProcessingJobState.Running ? linked.ProcessingState != DocumentProcessingState.Processing
                            : linked.ProcessingState != DocumentProcessingState.Finished)
                    : job.AttemptCount != 0)
                    return Conflict(LeaseOperationFailure.AttemptConflict);
                var now = await Now(db, ct);
                var availability = job.State != RequirementProcessingJobState.Running ? LeaseAvailability.Terminal
                    : job.CancellationRequestedAtUtc.HasValue ? LeaseAvailability.CancellationRequested
                    : !client.IsActive || !project.IsActive || !requirement.IsCurrent ? LeaseAvailability.ContextChanged
                    : job.LeaseExpiresAtUtc <= now ? LeaseAvailability.Expired
                    : job.LeaseExpiresAtUtc != prior.ExpiresAtUtc ? LeaseAvailability.Superseded : LeaseAvailability.Live;
                return new(LeaseOperationState.AlreadyApplied, Receipt: prior,
                    Observation: new(job.State, job.ProcessingAttemptId, now, job.LeaseExpiresAtUtc, availability));
            }
            if (observeOnly) return Unknown();
            if (!await ExperienceExecutionGuard.LiveAsync(db, job, ct))
                return Reject(LeaseOperationFailure.AuthorizationConflict);
            if (!client.IsActive || !project.IsActive) return Reject(LeaseOperationFailure.InactiveContext);
            if (job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2) return Reject(LeaseOperationFailure.WrongVersion);
            if (job.State is RequirementProcessingJobState.Cancelled or RequirementProcessingJobState.Failed or RequirementProcessingJobState.Succeeded)
                return Reject(LeaseOperationFailure.Terminal);
            if (job.CancellationRequestedAtUtc.HasValue) return Reject(LeaseOperationFailure.CancellationRequested);
            if (job.State == RequirementProcessingJobState.Running)
                return job.LeaseExpiresAtUtc <= await Now(db, ct) ? Reject(LeaseOperationFailure.Expired) : Conflict(LeaseOperationFailure.AlreadyOwned);
            if (!requirement.IsCurrent || requirement.Status != RequirementStatus.Pending || requirement.CommercialLine is null
                || !Enum.IsDefined(requirement.CommercialLine.Value) || job.State != RequirementProcessingJobState.Queued
                || job.LeaseGeneration != 0 || job.LeaseOwner is not null || job.LeaseExpiresAtUtc.HasValue
                || job.StartedAtUtc.HasValue || job.FinishedAtUtc.HasValue || job.LastErrorCode is not null
                || job.PublicationContractVersion is not null || job.PublicationContentHash is not null)
                return Conflict(LeaseOperationFailure.IdentityMismatch);
            if (job.AttemptCount != 0 || job.ProcessingAttemptId.HasValue
                || attempts.Any(x => x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
                return Conflict(LeaseOperationFailure.AttemptConflict);

            write.MutationStarted = true;
            var inserted = await db.JobLeaseOperations.FromSqlInterpolated($"""
                WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS t), changed AS (
                    UPDATE core.requirement_processing_jobs j
                    SET state = 'Running', started_at_utc = clock.t, lease_owner = {owner},
                        lease_generation = 1, lease_expires_at_utc = clock.t + interval '120 seconds'
                    FROM clock
                    WHERE j.id = {e.JobId} AND j.requirement_id = {e.RequirementId} AND j.execution_request_id = {e.ExecutionRequestId}
                      AND j.required_publication_version = {RequirementProcessingJob.PublicationV2}
                      AND j.state = 'Queued' AND j.lease_generation = 0 AND j.lease_owner IS NULL
                      AND j.lease_expires_at_utc IS NULL AND j.started_at_utc IS NULL AND j.finished_at_utc IS NULL
                      AND j.cancellation_requested_at_utc IS NULL AND j.last_error_code IS NULL
                      AND j.publication_contract_version IS NULL AND j.publication_content_hash IS NULL
                      AND j.attempt_count = 0 AND j.processing_attempt_id IS NULL AND clock.t >= j.created_at_utc
                      AND j.automatic_authorization_policy_version IS NOT DISTINCT FROM {job.AutomaticAuthorizationPolicyVersion}
                      AND j.authorized_actor_user_id IS NOT DISTINCT FROM {job.AuthorizedActorUserId}
                      AND j.authorized_at_utc IS NOT DISTINCT FROM {job.AuthorizedAtUtc}
                      AND j.authorization_expires_at_utc IS NOT DISTINCT FROM {job.AuthorizationExpiresAtUtc}
                      AND j.dispatch_admitted_at_utc IS NOT DISTINCT FROM {job.DispatchAdmittedAtUtc}
                      AND j.dispatch_instance_id IS NOT DISTINCT FROM {job.DispatchInstanceId}
                      AND j.dispatch_claim_operation_id IS NOT DISTINCT FROM {job.DispatchClaimOperationId}
                      AND (j.automatic_authorization_policy_version IS NULL OR (
                          j.authorization_revoked_at_utc IS NULL AND j.authorized_at_utc <= clock.t
                          AND clock.t < j.authorization_expires_at_utc))
                    RETURNING j.*
                )
                INSERT INTO core.job_lease_operations
                    (operation_id, kind, job_id, requirement_id, execution_request_id, actor_user_id,
                     claim_operation_id, lease_owner, lease_generation, policy_version, expected_lease_expires_at_utc,
                     expected_processing_attempt_id, assigned_at_utc, applied_at_utc, expires_at_utc)
                SELECT {e.OperationId}, 'Claim', id, requirement_id, execution_request_id, {actor},
                       {e.OperationId}, lease_owner, lease_generation, 1, NULL, NULL, started_at_utc, started_at_utc, lease_expires_at_utc
                FROM changed RETURNING *
                """).AsNoTracking().ToListAsync(ct);
            if (inserted.Count != 1)
            {
                await tx.RollbackAsync(ct);
                write.RolledBack = true;
                return Conflict(LeaseOperationFailure.IdentityMismatch);
            }
            var receipt = inserted[0];
            if (!Matches(receipt, e, actor, owner)) throw new InvalidOperationException("Invalid claim receipt.");
            write.Candidate = receipt;
            write.CommitAttempted = true;
            await tx.CommitAsync(ct);
            return new(LeaseOperationState.Claimed, Receipt: receipt,
                Observation: new(RequirementProcessingJobState.Running, null, receipt.AppliedAtUtc, receipt.ExpiresAtUtc, LeaseAvailability.Live));
        }
        catch
        {
            if (!write.CommitAttempted)
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await tx.RollbackAsync(rollback.Token); write.RolledBack = true; }
                catch (Exception) { /* Preserve uncertainty if rollback cannot be established. */ }
            }
            throw;
        }
    }

    private static Task<DateTimeOffset> Now(ApplicationDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private static bool Matches(JobLeaseOperation r, ClaimInitialLease e, Guid actor, string owner)
    {
        if (r.Kind != JobLeaseOperationKind.Claim || r.OperationId != e.OperationId || r.JobId != e.JobId
            || r.RequirementId != e.RequirementId || r.ExecutionRequestId != e.ExecutionRequestId || r.ActorUserId != actor
            || r.ClaimOperationId != e.OperationId || r.LeaseOwner != owner || r.ExpectedLeaseExpiresAtUtc.HasValue
            || r.ExpectedProcessingAttemptId.HasValue) return false;
        try
        {
            _ = JobLeaseOperation.CreateClaim(r.OperationId, r.JobId, r.RequirementId, r.ExecutionRequestId, r.ActorUserId,
                r.ClaimOperationId, r.LeaseOwner, r.LeaseGeneration, r.PolicyVersion, r.AssignedAtUtc, r.AppliedAtUtc, r.ExpiresAtUtc);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Equal(JobLeaseOperation a, JobLeaseOperation b) => a.OperationId == b.OperationId && a.Kind == b.Kind
        && a.JobId == b.JobId && a.RequirementId == b.RequirementId && a.ExecutionRequestId == b.ExecutionRequestId
        && a.ActorUserId == b.ActorUserId && a.ClaimOperationId == b.ClaimOperationId && a.LeaseOwner == b.LeaseOwner
        && a.LeaseGeneration == b.LeaseGeneration && a.PolicyVersion == b.PolicyVersion
        && a.ExpectedLeaseExpiresAtUtc == b.ExpectedLeaseExpiresAtUtc && a.ExpectedProcessingAttemptId == b.ExpectedProcessingAttemptId
        && a.AssignedAtUtc == b.AssignedAtUtc && a.AppliedAtUtc == b.AppliedAtUtc && a.ExpiresAtUtc == b.ExpiresAtUtc;
}
