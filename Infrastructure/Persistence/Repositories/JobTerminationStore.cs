using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class JobTerminationStore(DbContextOptions<ApplicationDbContext> options) : IJobTerminationStore
{
    private enum Operation { Request, Confirm, Fail }

    public Task<JobTerminationResult> FinalizeExecutionAsync(ExecutionTermination request, Guid actorUserId,
        string? errorCode, CancellationToken cancellationToken) =>
        ExecuteAsync(request.Execution, actorUserId, errorCode is null ? Operation.Confirm : Operation.Fail,
            errorCode, cancellationToken, request);

    public Task<JobTerminationResult> RequestJobCancellationAsync(JobCancellationRequest request, Guid actorUserId, CancellationToken cancellationToken) =>
        ExecuteAsync(new(request.JobId, request.ExecutionRequestId, request.RequirementId, "", 0), actorUserId, Operation.Request, null, cancellationToken);
    public Task<JobTerminationResult> ConfirmJobCancellationAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        ExecuteAsync(execution, actorUserId, Operation.Confirm, null, cancellationToken);
    public Task<JobTerminationResult> FinalizeJobFailedAsync(AssignedJobExecution execution, Guid actorUserId, string errorCode, CancellationToken cancellationToken) =>
        ExecuteAsync(execution, actorUserId, Operation.Fail, errorCode, cancellationToken);

    private async Task<JobTerminationResult> ExecuteAsync(AssignedJobExecution e, Guid actor, Operation op, string? error, CancellationToken ct,
        ExecutionTermination? executionContext = null)
    {
        var commitAttempted = false;
        try { return await RunAsync(e, actor, op, error, false, () => commitAttempted = true, ct, executionContext); }
        catch (Exception) when (commitAttempted)
        {
            // Independent, bounded observation: never reuse the caller's cancelled token or retry the mutation.
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var result = await RunAsync(e, actor, op, error, true, () => { }, observation.Token, executionContext);
                return result.State == JobTerminationState.AlreadyRecognized ? result : Unknown();
            }
            catch (Exception) { return Unknown(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Conflict(JobTerminationFailure.PersistenceError); }
        finally { if (executionContext is not null || op == Operation.Fail && e.Permit is not null) ct.ThrowIfCancellationRequested(); }
    }

    private static JobTerminationResult Conflict(JobTerminationFailure reason) => new(JobTerminationState.Conflict, reason);
    private static JobTerminationResult Unknown() => new(JobTerminationState.Indeterminate, JobTerminationFailure.InconsistentEvidence);
    private static JobTerminationResult Result(JobTerminationState state, RequirementProcessingJob job) =>
        new(state, JobTerminationFailure.None, new(job.Id, job.ExecutionRequestId, job.RequirementId,
            job.ProcessingAttemptId, job.CorrelationId, job.State.ToString(), job.CancellationRequestedAtUtc, job.FinishedAtUtc, job.LastErrorCode));

    private async Task<JobTerminationResult> RunAsync(AssignedJobExecution e, Guid actor, Operation op, string? error,
        bool observeOnly, Action beforeCommit, CancellationToken ct, ExecutionTermination? executionContext)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
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
        if (user is null) return Conflict(JobTerminationFailure.Unauthorized);
        if (!user.IsActive) return Conflict(JobTerminationFailure.InactiveUser);
        if (route is null) return Conflict(JobTerminationFailure.NotFound);
        var client = await db.Set<Client>().FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var project = await db.Set<Project>().FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.CreatedByUserId != actor) return Conflict(JobTerminationFailure.NotFound);
        if (op == Operation.Fail && !project.IsActive) return Conflict(JobTerminationFailure.InactiveProject);
        if (client is null) return Conflict(JobTerminationFailure.ContextConflict);
        if (op == Operation.Fail && !client.IsActive) return Conflict(JobTerminationFailure.InactiveClient);
        var quote = await db.PreQuotes.FromSqlInterpolated(
            $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var requirement = await db.Requirements.FromSqlInterpolated(
            $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var job = await db.RequirementProcessingJobs.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_jobs WHERE id = {e.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (job is null || requirement is null || quote is null || project.ClientId != route.ClientId
            || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id || job.RequirementId != requirement.Id
            || job.SubmissionId != route.SubmissionId)
            return Conflict(JobTerminationFailure.ContextConflict);
        if (job.ExecutionRequestId != e.ExecutionRequestId || job.RequirementId != e.RequirementId)
            return Conflict(JobTerminationFailure.IdentityConflict);
        if (job.SubmissionId.HasValue && (submission is null || submission.ClientId != client.Id
            || submission.ProjectId != project.Id || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id))
            return Conflict(JobTerminationFailure.ContextConflict);
        if (op != Operation.Request && (job.LeaseGeneration != e.LeaseGeneration || job.ProcessingAttemptId != e.ProcessingAttemptId))
            return Conflict(JobTerminationFailure.IdentityConflict);
        if (op == Operation.Fail && !JobFailureCodes.IsAllowed(error!)) return Conflict(JobTerminationFailure.InvalidRequest);
        if ((executionContext is not null || op == Operation.Fail)
            && (!ExperienceExecutionGuard.Matches(job, e.Permit, actor)
                || !ExperienceExecutionGuard.Assignment(e.Permit, e.LeaseOwner, e.LeaseGeneration)))
            return Conflict(JobTerminationFailure.AuthorizationConflict);
        var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirement.Id} ORDER BY id FOR UPDATE").ToListAsync(ct);
        var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
        if (job.ProcessingAttemptId.HasValue
            ? job.AttemptCount != 1 || attempt is null || attempt.CorrelationId != job.CorrelationId
                || attempt.StartedAtUtc is null || attempt.StartedAtUtc < job.StartedAtUtc
            : job.AttemptCount != 0)
            return Conflict(JobTerminationFailure.AttemptConflict);

        var terminal = job.State is RequirementProcessingJobState.Cancelled or RequirementProcessingJobState.Failed or RequirementProcessingJobState.Succeeded;
        if (terminal)
        {
            var expected = op == Operation.Fail ? RequirementProcessingJobState.Failed : RequirementProcessingJobState.Cancelled;
            if (job.State != expected) return Conflict(JobTerminationFailure.TerminalConflict);
            if (job.FinishedAtUtc is null || job.LeaseOwner is not null || job.LeaseExpiresAtUtc is not null
                || (expected == RequirementProcessingJobState.Cancelled && job.CancellationRequestedAtUtc is null)
                || (expected == RequirementProcessingJobState.Failed && job.CancellationRequestedAtUtc is not null)
                || (attempt is not null && (attempt.ProcessingState != DocumentProcessingState.Finished
                    || attempt.Outcome != (op == Operation.Fail ? DocumentProcessingOutcome.Failed : DocumentProcessingOutcome.Cancelled)
                    || attempt.CompletedAtUtc != job.FinishedAtUtc
                    || attempt.ErrorCode != job.LastErrorCode)))
                return Unknown();
            if (op == Operation.Fail && job.LastErrorCode != error) return Conflict(JobTerminationFailure.IdentityConflict);
            return Result(JobTerminationState.AlreadyRecognized, job);
        }
        if (job.State is not (RequirementProcessingJobState.Running or RequirementProcessingJobState.Queued))
            return Conflict(JobTerminationFailure.StateConflict);
        if (attempt is not null && attempt.ProcessingState != DocumentProcessingState.Processing)
            return Conflict(JobTerminationFailure.AttemptConflict);

        if (op == Operation.Request && job.CancellationRequestedAtUtc.HasValue)
            return Result(JobTerminationState.AlreadyRecognized, job);
        if (observeOnly) return Unknown();
        if ((executionContext is not null || op == Operation.Fail)
            && !await ExperienceExecutionGuard.LiveAsync(db, job, ct))
            return Conflict(JobTerminationFailure.AuthorizationConflict);

        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var oldState = job.State.ToString();
        var oldCancellation = job.CancellationRequestedAtUtc;
        if (op != Operation.Request)
        {
            if (job.State != RequirementProcessingJobState.Running) return Conflict(JobTerminationFailure.StateConflict);
            if (job.LeaseOwner != e.LeaseOwner || job.LeaseExpiresAtUtc <= now)
                return Conflict(JobTerminationFailure.LeaseConflict);
            if (op == Operation.Confirm && oldCancellation is null) return Conflict(JobTerminationFailure.CancellationRequired);
            if (op == Operation.Fail && oldCancellation.HasValue) return Conflict(JobTerminationFailure.CancellationRequested);
            if (attempts.Any(x => x.Id != job.ProcessingAttemptId && x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
                return Conflict(JobTerminationFailure.AttemptConflict);
            if (attempt is not null)
            {
                if (!requirement.IsCurrent || requirement.Status != RequirementStatus.Processing)
                    return Conflict(JobTerminationFailure.StateConflict);
                if (op == Operation.Confirm)
                {
                    attempt.Cancel(now);
                    requirement.MarkProcessingCancelled(now);
                }
                else
                {
                    attempt.Fail(error!, now);
                    requirement.MarkFailed(now);
                }
                quote.RegisterActivity(now);
            }
            if (op == Operation.Confirm) job.ConfirmCancellation(now);
            else job.Fail(error!, now);
            if (attempt is not null) await db.SaveChangesAsync(ct);
        }
        else job.RequestCancellation(now);

        // The detached job is written once. The terminal guard tests the OLD lease before clearing it.
        var explicitOwnerConfirm = executionContext is null && op == Operation.Confirm;
        var affected = op == Operation.Request
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE core.requirement_processing_jobs
                SET state = {job.State.ToString()}, cancellation_requested_at_utc = {job.CancellationRequestedAtUtc},
                    finished_at_utc = {job.FinishedAtUtc}
                WHERE id = {e.JobId} AND execution_request_id = {e.ExecutionRequestId} AND requirement_id = {e.RequirementId}
                    AND state = {oldState} AND cancellation_requested_at_utc IS NULL
                    AND processing_attempt_id IS NOT DISTINCT FROM {job.ProcessingAttemptId} AND attempt_count = {job.AttemptCount}
                """, ct)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE core.requirement_processing_jobs
                SET state = {job.State.ToString()}, finished_at_utc = {job.FinishedAtUtc}, last_error_code = {job.LastErrorCode},
                    lease_owner = NULL, lease_expires_at_utc = NULL
                WHERE id = {e.JobId} AND execution_request_id = {e.ExecutionRequestId} AND requirement_id = {e.RequirementId}
                    AND state = 'Running' AND lease_owner = {e.LeaseOwner} AND lease_generation = {e.LeaseGeneration}
                    AND processing_attempt_id IS NOT DISTINCT FROM {e.ProcessingAttemptId} AND attempt_count = {job.AttemptCount}
                    AND cancellation_requested_at_utc IS NOT DISTINCT FROM {oldCancellation}
                    AND lease_expires_at_utc > clock_timestamp()
                    AND ({explicitOwnerConfirm} OR (
                        automatic_authorization_policy_version IS NOT DISTINCT FROM {job.AutomaticAuthorizationPolicyVersion}
                        AND authorized_actor_user_id IS NOT DISTINCT FROM {job.AuthorizedActorUserId}
                        AND authorized_at_utc IS NOT DISTINCT FROM {job.AuthorizedAtUtc}
                        AND authorization_expires_at_utc IS NOT DISTINCT FROM {job.AuthorizationExpiresAtUtc}
                        AND dispatch_admitted_at_utc IS NOT DISTINCT FROM {job.DispatchAdmittedAtUtc}
                        AND dispatch_instance_id IS NOT DISTINCT FROM {job.DispatchInstanceId}
                        AND dispatch_claim_operation_id IS NOT DISTINCT FROM {job.DispatchClaimOperationId}
                        AND (automatic_authorization_policy_version IS NULL OR (
                            authorization_revoked_at_utc IS NULL AND authorized_at_utc <= clock_timestamp()
                            AND clock_timestamp() < authorization_expires_at_utc))))
                """, ct);
        if (affected != 1)
        {
            await tx.RollbackAsync(ct);
            return Conflict(op == Operation.Request ? JobTerminationFailure.StateConflict : JobTerminationFailure.LeaseConflict);
        }
        beforeCommit();
        await tx.CommitAsync(ct);
        return Result(op == Operation.Fail ? JobTerminationState.Failed
            : job.State == RequirementProcessingJobState.Cancelled ? JobTerminationState.CancellationConfirmed
            : JobTerminationState.CancellationRequested, job);
    }
}
