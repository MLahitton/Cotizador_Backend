using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class AssignedExecutionObservationStore(DbContextOptions<ApplicationDbContext> options)
    : IAssignedExecutionObservationStore
{
    public async Task<AssignedExecutionObservationResult> ObserveAsync(AssignedExecutionObservationRequest e,
        Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!ObserveAssignedExecutionService.Valid(e, actor)) return Reject(LeaseOperationFailure.InvalidRequest);
        AssignedExecutionObservationResult result;
        try { result = await ReadAsync(e, actor, ct); }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { result = new(AssignedExecutionObservationState.Unavailable); }
        ct.ThrowIfCancellationRequested();
        return result;
    }

    private static AssignedExecutionObservationResult Reject(LeaseOperationFailure f) =>
        new(AssignedExecutionObservationState.Rejected, f);

    private async Task<AssignedExecutionObservationResult> ReadAsync(AssignedExecutionObservationRequest e, Guid actor, CancellationToken ct)
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
                $"SELECT * FROM core.public_experience_submissions WHERE id={sid} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM identity.users WHERE id={actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) return Reject(LeaseOperationFailure.NotFound);
        if (!user.IsActive) return Reject(LeaseOperationFailure.InactiveUser);
        if (route is null) return Reject(LeaseOperationFailure.NotFound);
        var client = await db.Set<Client>().FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id={route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var project = await db.Set<Project>().FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id={route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.CreatedByUserId != actor) return Reject(LeaseOperationFailure.NotFound);
        // Same global order as writers; SHARE freezes the relevant protocol rows without DML.
        var quote = await db.PreQuotes.FromSqlInterpolated(
            $"SELECT * FROM core.pre_quotes WHERE id={route.QuoteId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var requirement = await db.Requirements.FromSqlInterpolated(
            $"SELECT * FROM core.requirements WHERE id={route.RequirementId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var job = await db.RequirementProcessingJobs.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_jobs WHERE id={e.JobId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (client is null || quote is null || requirement is null || job is null
            || project.ClientId != client.Id || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id
            || job.RequirementId != requirement.Id || job.SubmissionId != route.SubmissionId
            || job.RequirementId != e.RequirementId || job.ExecutionRequestId != e.ExecutionRequestId
            || (job.SubmissionId.HasValue && (submission is null || submission.ClientId != client.Id
                || submission.ProjectId != project.Id || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id)))
            return Reject(LeaseOperationFailure.IdentityMismatch);
        if (job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2) return Reject(LeaseOperationFailure.WrongVersion);
        if (!ExperienceExecutionGuard.Matches(job, e.Permit, actor)
            || (e.Permit is { } permit && e.ClaimOperationId != permit.Admission.ClaimOperationId)
            || !await ExperienceExecutionGuard.LiveAsync(db, job, ct))
            return Reject(LeaseOperationFailure.AuthorizationConflict);
        var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id={requirement.Id} ORDER BY id FOR SHARE")
            .AsNoTracking().ToListAsync(ct);
        var claim = await db.JobLeaseOperations.FromSqlInterpolated(
            $"SELECT * FROM core.job_lease_operations WHERE operation_id={e.ClaimOperationId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (claim is null || !Matches(claim, e, actor) || job.StartedAtUtc != claim.AssignedAtUtc
            || job.LeaseGeneration != claim.LeaseGeneration || job.State == RequirementProcessingJobState.Queued)
            return Reject(LeaseOperationFailure.ReceiptConflict);
        var terminal = job.State is RequirementProcessingJobState.Succeeded or RequirementProcessingJobState.Failed or RequirementProcessingJobState.Cancelled;
        if (terminal ? job.LeaseOwner is not null || job.LeaseExpiresAtUtc.HasValue || !job.FinishedAtUtc.HasValue
            : job.State != RequirementProcessingJobState.Running || job.LeaseOwner != claim.LeaseOwner
                || !job.LeaseExpiresAtUtc.HasValue || job.FinishedAtUtc.HasValue)
            return Reject(LeaseOperationFailure.IdentityMismatch);
        var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
        if (job.ProcessingAttemptId.HasValue
            ? job.AttemptCount != 1 || attempt is null || attempt.CorrelationId != job.CorrelationId
                || attempt.StartedAtUtc is null || attempt.StartedAtUtc < job.StartedAtUtc
                || (terminal ? attempt.ProcessingState != DocumentProcessingState.Finished
                    || attempt.CompletedAtUtc != job.FinishedAtUtc || attempt.ErrorCode != job.LastErrorCode
                    || (job.State == RequirementProcessingJobState.Failed ? attempt.Outcome != DocumentProcessingOutcome.Failed
                        : job.State == RequirementProcessingJobState.Cancelled ? attempt.Outcome != DocumentProcessingOutcome.Cancelled
                        : attempt.Outcome is not (DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview))
                    : attempt.ProcessingState != DocumentProcessingState.Processing)
            : job.AttemptCount != 0 || job.State == RequirementProcessingJobState.Succeeded)
            return Reject(LeaseOperationFailure.AttemptConflict);
        if (!terminal && (requirement.Status != (attempt is null ? RequirementStatus.Pending : RequirementStatus.Processing)
            || attempts.Any(x => x.Id != job.ProcessingAttemptId && x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing)))
            return Reject(LeaseOperationFailure.AttemptConflict);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        return new(AssignedExecutionObservationState.Observed, Snapshot: new(job.Id, requirement.Id, job.ExecutionRequestId,
            claim.OperationId, actor, job.State, terminal, job.CancellationRequestedAtUtc, job.ProcessingAttemptId,
            job.AttemptCount, attempt?.ProcessingState, attempt?.Outcome, claim.LeaseOwner, job.LeaseOwner,
            job.LeaseGeneration, claim.AssignedAtUtc, job.LeaseExpiresAtUtc, job.FinishedAtUtc, now,
            terminal ? ObservedExecutionLease.Released : job.LeaseExpiresAtUtc > now ? ObservedExecutionLease.Live : ObservedExecutionLease.Expired,
            client.IsActive && project.IsActive && requirement.IsCurrent ? ObservedExecutionAuthorization.ActiveContext
                : ObservedExecutionAuthorization.ContextChanged, requirement.Status));
    }

    private static bool Matches(JobLeaseOperation c, AssignedExecutionObservationRequest e, Guid actor)
    {
        if (c.Kind != JobLeaseOperationKind.Claim || c.OperationId != e.ClaimOperationId || c.ClaimOperationId != c.OperationId
            || c.JobId != e.JobId || c.RequirementId != e.RequirementId || c.ExecutionRequestId != e.ExecutionRequestId
            || c.ActorUserId != actor || c.ExpectedLeaseExpiresAtUtc.HasValue || c.ExpectedProcessingAttemptId.HasValue) return false;
        try
        {
            _ = JobLeaseOperation.CreateClaim(c.OperationId, c.JobId, c.RequirementId, c.ExecutionRequestId, c.ActorUserId,
                c.ClaimOperationId, c.LeaseOwner, c.LeaseGeneration, c.PolicyVersion, c.AssignedAtUtc, c.AppliedAtUtc, c.ExpiresAtUtc);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
