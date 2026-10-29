using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ExpiredCancellationStore(DbContextOptions<ApplicationDbContext> options) : IExpiredCancellationStore
{
    private sealed class WriteState
    {
        internal bool Mutating, CommitAttempted, RolledBack;
        internal ExpiredExecutionCancellation? Candidate;
    }

    public async Task<ExpiredClosureResult> CloseExpiredCancellationAsync(ExpiredCancellation request,
        Guid actorUserId, CancellationToken actionCt)
    {
        actionCt.ThrowIfCancellationRequested();
        if (!CloseExpiredCancellationService.Valid(request, actorUserId)) return Reject(JobTerminationFailure.InvalidRequest);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(actionCt);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        var write = new WriteState();
        try
        {
            try { return await RunAsync(request, actorUserId, write, false, budget.Token); }
            catch (Exception) when (write.CommitAttempted)
            {
                // RunAsync has disposed its context/transaction. Never repeat the mutation.
                using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    var found = await RunAsync(request, actorUserId, new(), true, observation.Token);
                    if (found.Result.State == JobTerminationState.AlreadyRecognized && found.Receipt is not null
                        && write.Candidate is not null && Equal(write.Candidate, found.Receipt)) return found;
                }
                catch (Exception) { /* Failed observation is not evidence of rollback. */ }
                return Unknown();
            }
            catch (PostgresException ex) when (write.RolledBack && ex.SqlState == PostgresErrorCodes.UniqueViolation)
            { return Reject(JobTerminationFailure.IdentityConflict); }
            catch (Exception)
            {
                return write.RolledBack ? Reject(JobTerminationFailure.PersistenceError) : Unknown();
            }
        }
        finally { actionCt.ThrowIfCancellationRequested(); }
    }

    private static ExpiredClosureResult Reject(JobTerminationFailure f) => new(new(JobTerminationState.Conflict, f));
    private static ExpiredClosureResult Unknown() => new(new(JobTerminationState.Indeterminate, JobTerminationFailure.InconsistentEvidence));
    private static ExpiredClosureResult Success(JobTerminationState state, ExpiredExecutionCancellation r) =>
        new(new(state, JobTerminationFailure.None, new(r.JobId, r.ExecutionRequestId, r.RequirementId,
            r.ExpectedProcessingAttemptId, r.CorrelationId, "Cancelled", r.CancellationRequestedAtUtc, r.FinishedAtUtc, null)), r);

    private async Task<ExpiredClosureResult> RunAsync(ExpiredCancellation e, Guid actor, WriteState write,
        bool observeOnly, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var id = e.Identity;
            var route = await (from j in db.RequirementProcessingJobs.AsNoTracking()
                join r in db.Requirements.AsNoTracking() on j.RequirementId equals r.Id
                join q in db.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
                join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
                where j.Id == id.JobId
                select new { j.SubmissionId, RequirementId = r.Id, QuoteId = q.Id, ProjectId = p.Id, p.ClientId }).SingleOrDefaultAsync(ct);
            PublicExperienceSubmission? submission = null;
            if (route?.SubmissionId is Guid sid)
                submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
                    $"SELECT * FROM core.public_experience_submissions WHERE id={sid} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var user = await db.Users.FromSqlInterpolated(
                $"SELECT * FROM identity.users WHERE id={actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (user is null) return Reject(JobTerminationFailure.NotFound);
            if (!user.IsActive) return Reject(JobTerminationFailure.InactiveUser);
            if (route is null) return Reject(JobTerminationFailure.NotFound);
            var client = await db.Set<Client>().FromSqlInterpolated(
                $"SELECT * FROM core.clients WHERE id={route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var project = await db.Set<Project>().FromSqlInterpolated(
                $"SELECT * FROM core.projects WHERE id={route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.CreatedByUserId != actor) return Reject(JobTerminationFailure.NotFound);
            var quote = await db.PreQuotes.FromSqlInterpolated(observeOnly
                ? (FormattableString)$"SELECT * FROM core.pre_quotes WHERE id={route.QuoteId} FOR SHARE"
                : $"SELECT * FROM core.pre_quotes WHERE id={route.QuoteId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var requirement = await db.Requirements.FromSqlInterpolated(observeOnly
                ? (FormattableString)$"SELECT * FROM core.requirements WHERE id={route.RequirementId} FOR SHARE"
                : $"SELECT * FROM core.requirements WHERE id={route.RequirementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            var job = await db.RequirementProcessingJobs.FromSqlInterpolated(observeOnly
                ? (FormattableString)$"SELECT * FROM core.requirement_processing_jobs WHERE id={id.JobId} FOR SHARE"
                : $"SELECT * FROM core.requirement_processing_jobs WHERE id={id.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (client is null || quote is null || requirement is null || job is null
                || project.ClientId != client.Id || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id
                || job.RequirementId != requirement.Id || job.SubmissionId != route.SubmissionId
                || (job.SubmissionId.HasValue && (submission is null || submission.ClientId != client.Id
                    || submission.ProjectId != project.Id || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id)))
                return Reject(JobTerminationFailure.ContextConflict);
            if (job.RequirementId != id.RequirementId || job.ExecutionRequestId != id.ExecutionRequestId)
                return Reject(JobTerminationFailure.IdentityConflict);
            if (job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2) return Reject(JobTerminationFailure.ContextConflict);
            var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(observeOnly
                ? (FormattableString)$"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id={requirement.Id} ORDER BY id FOR SHARE"
                : $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id={requirement.Id} ORDER BY id FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            var claim = await db.JobLeaseOperations.FromSqlInterpolated(
                $"SELECT * FROM core.job_lease_operations WHERE operation_id={id.ClaimOperationId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            var prior = await db.ExpiredExecutionCancellations.FromSqlInterpolated(
                $"SELECT * FROM core.expired_execution_cancellations WHERE operation_id={e.OperationId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (prior is not null && !Matches(prior, e, actor)) return Reject(JobTerminationFailure.IdentityConflict);
            if (claim is null || !MatchesClaim(claim, e) || job.StartedAtUtc != e.AssignedAtUtc
                || job.LeaseGeneration != e.LeaseGeneration) return Reject(JobTerminationFailure.IdentityConflict);
            var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
            if (job.ProcessingAttemptId != e.ExpectedProcessingAttemptId) return Reject(JobTerminationFailure.AttemptConflict);
            if (job.ProcessingAttemptId.HasValue
                ? job.AttemptCount != 1 || attempt is null || attempt.CorrelationId != job.CorrelationId
                    || attempt.StartedAtUtc is null || attempt.CreatedAtUtc > attempt.StartedAtUtc || attempt.StartedAtUtc < job.StartedAtUtc
                : job.AttemptCount != 0) return Reject(JobTerminationFailure.AttemptConflict);
            if (prior is not null)
            {
                if (prior.CorrelationId != job.CorrelationId || job.State != RequirementProcessingJobState.Cancelled
                    || job.FinishedAtUtc != prior.FinishedAtUtc || job.CancellationRequestedAtUtc != prior.CancellationRequestedAtUtc
                    || job.LeaseOwner is not null || job.LeaseExpiresAtUtc is not null || job.LastErrorCode is not null
                    || job.PublicationContractVersion is not null || job.PublicationContentHash is not null
                    || (attempt is not null && (attempt.ProcessingState != DocumentProcessingState.Finished
                        || attempt.Outcome != DocumentProcessingOutcome.Cancelled || attempt.CompletedAtUtc != prior.FinishedAtUtc
                        || attempt.ErrorCode is not null))) return Reject(JobTerminationFailure.InconsistentEvidence);
                return Success(JobTerminationState.AlreadyRecognized, prior);
            }
            if (observeOnly) return Unknown();
            if (job.State is RequirementProcessingJobState.Cancelled or RequirementProcessingJobState.Failed or RequirementProcessingJobState.Succeeded)
                return Reject(JobTerminationFailure.TerminalConflict);
            if (job.State != RequirementProcessingJobState.Running || !requirement.IsCurrent
                || requirement.Status != (attempt is null ? RequirementStatus.Pending : RequirementStatus.Processing))
                return Reject(JobTerminationFailure.StateConflict);
            if (job.CancellationRequestedAtUtc is null) return Reject(JobTerminationFailure.CancellationRequired);
            if (job.CancellationRequestedAtUtc != e.ExpectedCancellationRequestedAtUtc || job.LeaseOwner != e.LeaseOwner)
                return Reject(JobTerminationFailure.IdentityConflict);
            if (job.LeaseExpiresAtUtc != e.ExpectedLeaseExpiresAtUtc) return Reject(JobTerminationFailure.LeaseConflict);
            if (job.FinishedAtUtc is not null || job.LastErrorCode is not null
                || job.PublicationContractVersion is not null || job.PublicationContentHash is not null)
                return Reject(JobTerminationFailure.InconsistentEvidence);
            if (attempt is null ? attempts.Count != 0
                : attempt.ProcessingState != DocumentProcessingState.Processing || attempt.CompletedAtUtc is not null
                    || attempt.Outcome is not null || attempt.ErrorCode is not null
                    || attempts.Any(x => x.Id != attempt.Id && x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
                return Reject(JobTerminationFailure.AttemptConflict);
            if (attempt is not null && (await db.RequirementExtractionResults.AsNoTracking().AnyAsync(x => x.RequirementProcessingAttemptId == attempt.Id, ct)
                || await db.RequirementTechnicalProposals.AsNoTracking().AnyAsync(x => x.RequirementProcessingAttemptId == attempt.Id, ct)))
                return Reject(JobTerminationFailure.InconsistentEvidence);
            var t0 = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
            if (e.ExpectedLeaseExpiresAtUtc > t0) return Reject(JobTerminationFailure.LeaseConflict);
            if (job.CreatedAtUtc > t0 || job.StartedAtUtc > t0 || job.CancellationRequestedAtUtc > t0
                || requirement.CreatedAtUtc > t0 || requirement.UpdatedAtUtc > t0 || quote.CreatedAtUtc > t0 || quote.UpdatedAtUtc > t0
                || attempt?.CreatedAtUtc > t0 || attempt?.StartedAtUtc > t0) return Reject(JobTerminationFailure.IdentityConflict);
            job.ConfirmCancellation(t0);
            if (attempt is not null)
            {
                // Attach only these independent entities, never the detached job or a navigation graph.
                db.Attach(attempt); db.Attach(requirement); db.Attach(quote);
                attempt.Cancel(t0); requirement.MarkProcessingCancelled(t0); quote.RegisterActivity(t0);
                write.Mutating = true;
                await db.SaveChangesAsync(ct);
            }
            write.Mutating = true;
            var inserted = await db.ExpiredExecutionCancellations.FromSqlInterpolated($"""
                WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS g), changed AS (
                    UPDATE core.requirement_processing_jobs j
                    SET state='Cancelled', finished_at_utc={t0}, lease_owner=NULL, lease_expires_at_utc=NULL
                    FROM clock
                    WHERE j.id={id.JobId} AND j.requirement_id={id.RequirementId} AND j.execution_request_id={id.ExecutionRequestId}
                      AND j.correlation_id={job.CorrelationId} AND j.required_publication_version={RequirementProcessingJob.PublicationV2}
                      AND j.state='Running' AND j.started_at_utc={e.AssignedAtUtc}
                      AND j.lease_owner={e.LeaseOwner} AND j.lease_generation={e.LeaseGeneration}
                      AND j.lease_expires_at_utc={e.ExpectedLeaseExpiresAtUtc} AND j.lease_expires_at_utc<=clock.g AND clock.g>={t0}
                      AND j.processing_attempt_id IS NOT DISTINCT FROM {e.ExpectedProcessingAttemptId} AND j.attempt_count={job.AttemptCount}
                      AND j.cancellation_requested_at_utc IS NOT NULL AND j.cancellation_requested_at_utc={e.ExpectedCancellationRequestedAtUtc}
                      AND j.finished_at_utc IS NULL AND j.last_error_code IS NULL
                      AND j.publication_contract_version IS NULL AND j.publication_content_hash IS NULL
                    RETURNING j.*, clock.g AS guarded
                )
                INSERT INTO core.expired_execution_cancellations
                    (operation_id, job_id, requirement_id, execution_request_id, claim_operation_id,
                     closed_by_actor_user_id, correlation_id, lease_owner, lease_generation, assigned_at_utc,
                     expected_lease_expires_at_utc, expected_processing_attempt_id, cancellation_requested_at_utc,
                     finished_at_utc, guard_at_utc, policy_version)
                SELECT {e.OperationId}, id, requirement_id, execution_request_id, {id.ClaimOperationId},
                       {actor}, correlation_id, {e.LeaseOwner}, lease_generation, started_at_utc,
                       {e.ExpectedLeaseExpiresAtUtc}, processing_attempt_id, cancellation_requested_at_utc,
                       finished_at_utc, guarded, 1 FROM changed RETURNING *
                """).AsNoTracking().ToListAsync(ct);
            if (inserted.Count != 1)
            {
                var current = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == id.JobId, ct);
                var identityChanged = current.RequirementId != id.RequirementId || current.ExecutionRequestId != id.ExecutionRequestId
                    || current.CorrelationId != job.CorrelationId || current.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2
                    || current.State != RequirementProcessingJobState.Running || current.StartedAtUtc != e.AssignedAtUtc
                    || current.LeaseOwner != e.LeaseOwner || current.LeaseGeneration != e.LeaseGeneration
                    || current.ProcessingAttemptId != e.ExpectedProcessingAttemptId || current.AttemptCount != job.AttemptCount
                    || current.CancellationRequestedAtUtc != e.ExpectedCancellationRequestedAtUtc || current.FinishedAtUtc is not null
                    || current.LastErrorCode is not null || current.PublicationContractVersion is not null || current.PublicationContentHash is not null;
                // A later clock sample cannot explain the guard's G. With identities unchanged, its time/expiry guard lost.
                var failure = current.LeaseExpiresAtUtc != e.ExpectedLeaseExpiresAtUtc || !identityChanged
                    ? JobTerminationFailure.LeaseConflict : JobTerminationFailure.IdentityConflict;
                await tx.RollbackAsync(ct); write.RolledBack = true;
                return Reject(failure);
            }
            var receipt = inserted[0];
            if (!Matches(receipt, e, actor) || receipt.CorrelationId != job.CorrelationId || receipt.FinishedAtUtc != t0)
                throw new InvalidOperationException("Invalid expired cancellation receipt.");
            write.Candidate = receipt; write.CommitAttempted = true;
            await tx.CommitAsync(ct);
            return Success(JobTerminationState.CancellationConfirmed, receipt);
        }
        catch
        {
            if (!write.CommitAttempted)
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await tx.RollbackAsync(rollback.Token); write.RolledBack = true; }
                catch (Exception) { /* No established rollback: preserve uncertainty. */ }
            }
            throw;
        }
    }

    private static bool MatchesClaim(JobLeaseOperation c, ExpiredCancellation e)
    {
        if (c.Kind != JobLeaseOperationKind.Claim || c.OperationId != e.Identity.ClaimOperationId || c.ClaimOperationId != c.OperationId
            || c.JobId != e.Identity.JobId || c.RequirementId != e.Identity.RequirementId || c.ExecutionRequestId != e.Identity.ExecutionRequestId
            || c.LeaseOwner != e.LeaseOwner || c.LeaseGeneration != e.LeaseGeneration || c.AssignedAtUtc != e.AssignedAtUtc
            || c.ExpectedLeaseExpiresAtUtc is not null || c.ExpectedProcessingAttemptId is not null) return false;
        try
        {
            _ = JobLeaseOperation.CreateClaim(c.OperationId, c.JobId, c.RequirementId, c.ExecutionRequestId, c.ActorUserId,
                c.ClaimOperationId, c.LeaseOwner, c.LeaseGeneration, c.PolicyVersion, c.AssignedAtUtc, c.AppliedAtUtc, c.ExpiresAtUtc);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Matches(ExpiredExecutionCancellation r, ExpiredCancellation e, Guid actor)
    {
        if (r.OperationId != e.OperationId || r.JobId != e.Identity.JobId || r.RequirementId != e.Identity.RequirementId
            || r.ExecutionRequestId != e.Identity.ExecutionRequestId || r.ClaimOperationId != e.Identity.ClaimOperationId
            || r.ClosedByActorUserId != actor || r.LeaseOwner != e.LeaseOwner || r.LeaseGeneration != e.LeaseGeneration
            || r.AssignedAtUtc != e.AssignedAtUtc || r.ExpectedLeaseExpiresAtUtc != e.ExpectedLeaseExpiresAtUtc
            || r.ExpectedProcessingAttemptId != e.ExpectedProcessingAttemptId || r.CancellationRequestedAtUtc != e.ExpectedCancellationRequestedAtUtc)
            return false;
        try
        {
            _ = ExpiredExecutionCancellation.Create(r.OperationId, r.JobId, r.RequirementId, r.ExecutionRequestId,
                r.ClaimOperationId, r.ClosedByActorUserId, r.CorrelationId, r.LeaseOwner, r.LeaseGeneration,
                r.AssignedAtUtc, r.ExpectedLeaseExpiresAtUtc, r.ExpectedProcessingAttemptId, r.CancellationRequestedAtUtc,
                r.FinishedAtUtc, r.GuardAtUtc, r.PolicyVersion);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Equal(ExpiredExecutionCancellation a, ExpiredExecutionCancellation b) =>
        a.OperationId == b.OperationId && a.JobId == b.JobId && a.RequirementId == b.RequirementId
        && a.ExecutionRequestId == b.ExecutionRequestId && a.ClaimOperationId == b.ClaimOperationId
        && a.ClosedByActorUserId == b.ClosedByActorUserId && a.CorrelationId == b.CorrelationId
        && a.LeaseOwner == b.LeaseOwner && a.LeaseGeneration == b.LeaseGeneration && a.PolicyVersion == b.PolicyVersion
        && a.AssignedAtUtc == b.AssignedAtUtc && a.ExpectedLeaseExpiresAtUtc == b.ExpectedLeaseExpiresAtUtc
        && a.ExpectedProcessingAttemptId == b.ExpectedProcessingAttemptId && a.CancellationRequestedAtUtc == b.CancellationRequestedAtUtc
        && a.FinishedAtUtc == b.FinishedAtUtc && a.GuardAtUtc == b.GuardAtUtc;
}
