using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class AssignedJobStartStore(DbContextOptions<ApplicationDbContext> options,
    string requiredPublicationVersion = RequirementProcessingJob.PublicationV1) : IAssignedJobStartStore
{
    public Task<AssignedJobStartResult> StartVerifiedAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) =>
        new AssignedJobStartStore(options, RequirementProcessingJob.PublicationV2).StartAsync(execution, actorUserId, cancellationToken);

    public async Task<AssignedJobStartResult> StartAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken)
    {
        var commitAttempted = false;
        try
        {
            return await RunAsync(execution, actorUserId, false, () => commitAttempted = true, cancellationToken);
        }
        catch (Exception) when (commitAttempted)
        {
            // RunAsync has disposed its transaction/context before this independent observation.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var recognized = await RecognizeAsync(execution, actorUserId, cleanup.Token);
            return recognized.State == AssignedJobStartState.AlreadyStarted ? recognized : Unknown();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return AssignedJobStartResult.Reject(AssignedJobStartFailure.PersistenceError); }
        finally { if (execution.Permit is not null) cancellationToken.ThrowIfCancellationRequested(); }
    }

    public async Task<AssignedJobStartResult> RecognizeAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken)
    {
        try { return await RunAsync(execution, actorUserId, true, () => { }, cancellationToken); }
        catch (Exception) { return Unknown(); }
        finally { if (execution.Permit is not null) cancellationToken.ThrowIfCancellationRequested(); }
    }

    private static AssignedJobStartResult Unknown() => new(AssignedJobStartState.Indeterminate, AssignedJobStartFailure.InconsistentEvidence);

    private async Task<AssignedJobStartResult> RunAsync(AssignedJobExecution e, Guid actor, bool readOnly,
        Action beforeCommit, CancellationToken ct)
    {
        if (requiredPublicationVersion is not (RequirementProcessingJob.PublicationV1 or RequirementProcessingJob.PublicationV2))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidRequest);
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Discovery is not authorization. Every edge is checked again after ordered locks.
        var route = await (from job in db.RequirementProcessingJobs.AsNoTracking()
            join requirement in db.Requirements.AsNoTracking() on job.RequirementId equals requirement.Id
            join quote in db.PreQuotes.AsNoTracking() on requirement.PreQuoteId equals quote.Id
            join project in db.Projects.AsNoTracking() on quote.ProjectId equals project.Id
            where job.Id == e.JobId
            select new { job.SubmissionId, RequirementId = requirement.Id, QuoteId = quote.Id,
                ProjectId = project.Id, project.ClientId }).SingleOrDefaultAsync(ct);
        PublicExperienceSubmission? submission = null;
        if (route?.SubmissionId is Guid submissionId)
            submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
                $"SELECT * FROM core.public_experience_submissions WHERE id = {submissionId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM identity.users WHERE id = {actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) return AssignedJobStartResult.Reject(AssignedJobStartFailure.Unauthorized);
        if (!user.IsActive) return AssignedJobStartResult.Reject(AssignedJobStartFailure.InactiveUser);
        if (route is null) return AssignedJobStartResult.Reject(AssignedJobStartFailure.NotFound);
        var client = await db.Set<Client>().FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var projectRow = await db.Set<Project>().FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (projectRow is null || (projectRow.CreatedByUserId != actor
            && !(readOnly && requiredPublicationVersion == RequirementProcessingJob.PublicationV1 && user.Role == UserRole.Admin)))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.NotFound);
        if (!projectRow.IsActive) return AssignedJobStartResult.Reject(AssignedJobStartFailure.InactiveProject);
        if (client is null || !client.IsActive) return AssignedJobStartResult.Reject(AssignedJobStartFailure.InactiveClient);
        var quoteRow = await db.PreQuotes.FromSqlInterpolated(
            $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var requirementRow = await db.Requirements.FromSqlInterpolated(
            $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var jobRow = await db.RequirementProcessingJobs.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_jobs WHERE id = {e.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (jobRow is null || requirementRow is null || quoteRow is null
            || projectRow.ClientId != route.ClientId || quoteRow.ProjectId != projectRow.Id
            || requirementRow.PreQuoteId != quoteRow.Id || jobRow.RequirementId != requirementRow.Id
            || jobRow.SubmissionId != route.SubmissionId)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.ContextConflict);
        if (jobRow.RequirementId != e.RequirementId || jobRow.ExecutionRequestId != e.ExecutionRequestId)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.IdentityConflict);
        if (jobRow.RequiredPublicationVersion != requiredPublicationVersion)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.StateConflict);
        if (jobRow.SubmissionId.HasValue && (submission is null || submission.RequirementId != requirementRow.Id
            || submission.PreQuoteId != quoteRow.Id || submission.ProjectId != projectRow.Id || submission.ClientId != client.Id))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.ContextConflict);
        if (!ExperienceExecutionGuard.Matches(jobRow, e.Permit, actor)
            || !ExperienceExecutionGuard.Assignment(e.Permit, e.LeaseOwner, e.LeaseGeneration))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.AuthorizationConflict);
        var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirementRow.Id} ORDER BY id FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        if (requiredPublicationVersion == RequirementProcessingJob.PublicationV2)
        {
            // Claim authority also applies to recognition; current ownership alone cannot adopt an assignment.
            var claim = await db.JobLeaseOperations.FromSqlInterpolated(
                $"SELECT * FROM core.job_lease_operations WHERE job_id = {jobRow.Id} AND kind = 'Claim' FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (claim is null || !MatchesClaim(claim, jobRow, e, actor))
                return AssignedJobStartResult.Reject(AssignedJobStartFailure.IdentityConflict);
        }
        if (jobRow.ProcessingAttemptId is Guid linked)
        {
            if (jobRow.LeaseGeneration != e.LeaseGeneration
                || (e.ProcessingAttemptId.HasValue && e.ProcessingAttemptId != linked)
                || (jobRow.State == RequirementProcessingJobState.Running && jobRow.LeaseOwner != e.LeaseOwner))
                return AssignedJobStartResult.Reject(AssignedJobStartFailure.IdentityConflict);
            var prior = attempts.SingleOrDefault(x => x.Id == linked);
            if (jobRow.AttemptCount != 1 || prior is null || prior.StartedAtUtc is null
                || prior.ProcessingState == DocumentProcessingState.Pending || prior.CorrelationId != jobRow.CorrelationId
                || jobRow.State == RequirementProcessingJobState.Queued
                || (jobRow.State == RequirementProcessingJobState.Running && prior.ProcessingState != DocumentProcessingState.Processing)
                || (jobRow.State != RequirementProcessingJobState.Running && prior.ProcessingState != DocumentProcessingState.Finished)
                || (jobRow.State == RequirementProcessingJobState.Succeeded && prior.Outcome is not (DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview))
                || (jobRow.State == RequirementProcessingJobState.Failed && prior.Outcome != DocumentProcessingOutcome.Failed)
                || (jobRow.State == RequirementProcessingJobState.Cancelled && prior.Outcome != DocumentProcessingOutcome.Cancelled)
                || prior.StartedAtUtc < jobRow.StartedAtUtc)
                return Unknown();
            return new(AssignedJobStartState.AlreadyStarted, AssignedJobStartFailure.None,
                Receipt(jobRow, prior, projectRow.Id, quoteRow.Id));
        }
        if (jobRow.AttemptCount != 0 || e.ProcessingAttemptId.HasValue)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.IdentityConflict);
        if (readOnly) return AssignedJobStartResult.Reject(AssignedJobStartFailure.NotStarted);
        if (!await ExperienceExecutionGuard.LiveAsync(db, jobRow, ct))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.AuthorizationConflict);
        if (jobRow.CancellationRequestedAtUtc.HasValue)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.CancellationRequested);
        if (jobRow.State != RequirementProcessingJobState.Running)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.StateConflict);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if (jobRow.LeaseOwner != e.LeaseOwner || jobRow.LeaseGeneration != e.LeaseGeneration || jobRow.LeaseExpiresAtUtc <= now)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.LeaseConflict);
        if (!requirementRow.IsCurrent || requirementRow.Status != RequirementStatus.Pending || requirementRow.CommercialLine is null)
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.StateConflict);
        if (attempts.Any(x => x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.ActiveAttempt);
        var files = await db.RequirementFiles.AsNoTracking().Where(x => x.RequirementId == requirementRow.Id).ToListAsync(ct);
        if (files.Count == 0 || files.Any(x => x.SizeBytes <= 0 || string.IsNullOrWhiteSpace(x.StorageKey)
            || string.IsNullOrWhiteSpace(x.ContentType) || string.IsNullOrWhiteSpace(x.OriginalFileName)))
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidDocuments);
        var attemptRow = RequirementProcessingAttempt.Create(requirementRow.Id, actor, jobRow.CorrelationId, now);
        attemptRow.Start(now);
        jobRow.LinkStartedAttempt(attemptRow.Id);
        requirementRow.StartProcessing(now);
        quoteRow.RegisterActivity(now);
        db.RequirementProcessingAttempts.Add(attemptRow);
        await db.SaveChangesAsync(ct);
        // Job is detached: this is its only write, and no SaveChanges follows it.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE core.requirement_processing_jobs SET processing_attempt_id = {attemptRow.Id}, attempt_count = {jobRow.AttemptCount}
            WHERE id = {e.JobId} AND execution_request_id = {e.ExecutionRequestId} AND requirement_id = {e.RequirementId}
              AND state = 'Running' AND lease_owner = {e.LeaseOwner} AND lease_generation = {e.LeaseGeneration}
              AND cancellation_requested_at_utc IS NULL AND processing_attempt_id IS NULL AND attempt_count = 0
              AND lease_expires_at_utc > clock_timestamp()
              AND required_publication_version = {requiredPublicationVersion}
              AND automatic_authorization_policy_version IS NOT DISTINCT FROM {jobRow.AutomaticAuthorizationPolicyVersion}
              AND authorized_actor_user_id IS NOT DISTINCT FROM {jobRow.AuthorizedActorUserId}
              AND authorized_at_utc IS NOT DISTINCT FROM {jobRow.AuthorizedAtUtc}
              AND authorization_expires_at_utc IS NOT DISTINCT FROM {jobRow.AuthorizationExpiresAtUtc}
              AND dispatch_admitted_at_utc IS NOT DISTINCT FROM {jobRow.DispatchAdmittedAtUtc}
              AND dispatch_instance_id IS NOT DISTINCT FROM {jobRow.DispatchInstanceId}
              AND dispatch_claim_operation_id IS NOT DISTINCT FROM {jobRow.DispatchClaimOperationId}
              AND (automatic_authorization_policy_version IS NULL OR (
                  authorization_revoked_at_utc IS NULL AND authorized_at_utc <= clock_timestamp()
                  AND clock_timestamp() < authorization_expires_at_utc))
            """, ct);
        if (affected != 1)
        {
            await tx.RollbackAsync(ct);
            return AssignedJobStartResult.Reject(AssignedJobStartFailure.LeaseConflict);
        }
        beforeCommit();
        await tx.CommitAsync(ct);
        return new(AssignedJobStartState.Started, AssignedJobStartFailure.None, Receipt(jobRow, attemptRow, projectRow.Id, quoteRow.Id));
    }

    private static bool MatchesClaim(JobLeaseOperation c, RequirementProcessingJob job, AssignedJobExecution e, Guid actor)
    {
        if (c.Kind != JobLeaseOperationKind.Claim || c.ClaimOperationId != c.OperationId
            || c.JobId != job.Id || c.RequirementId != job.RequirementId || c.ExecutionRequestId != job.ExecutionRequestId
            || c.ActorUserId != actor || c.LeaseOwner != e.LeaseOwner || c.LeaseGeneration != e.LeaseGeneration
            || c.LeaseGeneration != job.LeaseGeneration || c.AssignedAtUtc != job.StartedAtUtc
            || (job.State == RequirementProcessingJobState.Running && c.LeaseOwner != job.LeaseOwner)
            || c.ExpectedLeaseExpiresAtUtc.HasValue || c.ExpectedProcessingAttemptId.HasValue) return false;
        try
        {
            _ = JobLeaseOperation.CreateClaim(c.OperationId, c.JobId, c.RequirementId, c.ExecutionRequestId, c.ActorUserId,
                c.ClaimOperationId, c.LeaseOwner, c.LeaseGeneration, c.PolicyVersion, c.AssignedAtUtc, c.AppliedAtUtc, c.ExpiresAtUtc);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static AssignedJobStartReceipt Receipt(RequirementProcessingJob job, RequirementProcessingAttempt attempt,
        Guid projectId, Guid quoteId) => new(job.Id, job.ExecutionRequestId, job.RequirementId,
            attempt.Id, job.CorrelationId, projectId, quoteId, attempt.StartedAtUtc!.Value);
}
