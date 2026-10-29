using System.Data;
using Application.Projects.GetProjectWorkspace;
using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class WorkspaceExtractionReader(DbContextOptions<ApplicationDbContext> options) : IWorkspaceExtractionReader
{
    public async Task<ProjectWorkspaceWorkflowResult?> ReadCurrentAsync(Guid projectId, Guid preQuoteId, Guid actor, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor && x.IsActive, ct);
        if (user is null) return null;
        var allowed = await (from q in db.PreQuotes.AsNoTracking()
            join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
            join c in db.Clients.AsNoTracking() on p.ClientId equals c.Id
            where q.Id == preQuoteId && p.Id == projectId && (p.CreatedByUserId == actor || user.Role == UserRole.Admin)
            select q.Id).AnyAsync(ct);
        if (!allowed) return null;
        var quotes = await new PreQuoteRepository(db).ListWorkspaceCandidatesByProjectIdAsync(projectId, 2, ct);
        if (quotes.Count != 1)
            return new(quotes.Count == 0 ? ProjectWorkspaceResolutionState.Empty : ProjectWorkspaceResolutionState.Ambiguous,
                null, null, null, null, false);
        preQuoteId = quotes[0].Id;
        // Selection and all dependent evidence use this snapshot, never the earlier workspace selection.
        var current = await new RequirementRepository(db).GetCurrentByPreQuoteIdAsync(preQuoteId, ct);
        if (current is null) return new(ProjectWorkspaceResolutionState.Resolved, preQuoteId, null, null, null, false);
        var extraction = await ReadAsync(db, projectId, current.RequirementId, actor, ct);
        if (extraction is null) return null;
        var proposalId = extraction.State == "Manual" ? current.TechnicalProposalId : extraction.TechnicalProposalId;
        return new(ProjectWorkspaceResolutionState.Resolved, preQuoteId, current.RequirementId,
            current.Status.ToString().ToUpperInvariant(), proposalId,
            extraction.State == "Manual" ? current.HasTechnicalProposal : proposalId is not null, extraction);
    }

    public async Task<WorkspaceExtraction?> ReadAsync(Guid projectId, Guid requirementId, Guid actor, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        return await ReadAsync(db, projectId, requirementId, actor, ct);
    }

    private static async Task<WorkspaceExtraction?> ReadAsync(ApplicationDbContext db, Guid projectId, Guid requirementId, Guid actor, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor && x.IsActive, ct);
        if (user is null) return null;
        var route = await (from r in db.Requirements.AsNoTracking()
            join q in db.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
            join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
            join c in db.Clients.AsNoTracking() on p.ClientId equals c.Id
            where r.Id == requirementId && p.Id == projectId
                && (p.CreatedByUserId == actor || user.Role == UserRole.Admin)
            select new { Requirement = r, Owner = p.CreatedByUserId, Active = p.IsActive && c.IsActive }).SingleOrDefaultAsync(ct);
        if (route is null) return null;
        var jobs = await db.RequirementProcessingJobs.AsNoTracking().Where(x => x.RequirementId == requirementId).ToListAsync(ct);
        var attempts = await db.RequirementProcessingAttempts.AsNoTracking().Where(x => x.RequirementId == requirementId).ToListAsync(ct);
        var automatic = jobs.Any(x => x.AutomaticAuthorizationPolicyVersion is not null);
        if (!automatic)
        {
            var canAnalyze = jobs.Count == 0 && route.Owner == actor && route.Active && route.Requirement.IsCurrent
                && route.Requirement.CommercialLine is not null
                && route.Requirement.Status is RequirementStatus.Pending or RequirementStatus.Failed
                && !attempts.Any(x => x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing)
                && await db.RequirementFiles.AnyAsync(x => x.RequirementId == requirementId, ct);
            return new("Manual", false, canAnalyze);
        }
        if (jobs.Count != 1 || !route.Requirement.IsCurrent) return new("Inconsistent", false, false);
        var job = jobs[0];
        var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
        if (job.AutomaticAuthorizationPolicyVersion != 1 || job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2
            || !await db.Set<Domain.PublicExperience.PublicExperienceSubmission>().AnyAsync(x => x.Id == job.SubmissionId
                && x.ProjectId == projectId && x.PreQuoteId == route.Requirement.PreQuoteId && x.RequirementId == requirementId, ct)
            || job.AttemptCount != (attempt is null ? 0 : 1)
            || (job.ProcessingAttemptId is not null && (attempt is null || attempt.CorrelationId != job.CorrelationId)))
            return new("Inconsistent", false, false);
        if (job.State == RequirementProcessingJobState.Succeeded)
        {
            var proposals = await db.RequirementTechnicalProposals.AsNoTracking()
                .Where(x => x.RequirementId == requirementId).Select(x => new { x.Id, x.RequirementProcessingAttemptId, x.RequirementExtractionResultId }).ToListAsync(ct);
            var proposal = proposals.Count == 1 ? proposals[0] : null;
            var exact = route.Requirement.Status == RequirementStatus.Processed
                && attempt is not null && attempt.ProcessingState == DocumentProcessingState.Finished
                && attempt.Outcome is DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview
                && attempt.CompletedAtUtc == job.FinishedAtUtc && proposal is not null
                && proposal.RequirementProcessingAttemptId == attempt.Id
                && job.PublicationContractVersion == RequirementProcessingJob.PublicationV2
                && !string.IsNullOrWhiteSpace(job.PublicationContentHash)
                && await db.RequirementExtractionResults.AnyAsync(x => x.Id == proposal.RequirementExtractionResultId
                    && x.RequirementProcessingAttemptId == attempt.Id, ct);
            return exact ? new("Succeeded", false, false, proposal!.Id) : new("Inconsistent", false, false);
        }
        if (job.State is RequirementProcessingJobState.Failed or RequirementProcessingJobState.Cancelled)
        {
            if (attempt is not null && (attempt.ProcessingState != DocumentProcessingState.Finished
                || attempt.CompletedAtUtc != job.FinishedAtUtc
                || attempt.Outcome != (job.State == RequirementProcessingJobState.Failed ? DocumentProcessingOutcome.Failed : DocumentProcessingOutcome.Cancelled)))
                return new("Inconsistent", false, false);
            return new(job.State.ToString(), false, false);
        }
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var live = route.Active && job.AuthorizedActorUserId == route.Owner
            && await db.Users.AnyAsync(x => x.Id == route.Owner && x.IsActive, ct)
            && job.AuthorizationRevokedAtUtc is null && job.AuthorizedAtUtc <= now && job.AuthorizationExpiresAtUtc > now
            && (job.State != RequirementProcessingJobState.Running || job.LeaseExpiresAtUtc > now);
        if (job.CancellationRequestedAtUtc is not null) return new("CancellationRequested", live, false);
        if (!live) return new("ContinuityUnknown", false, false);
        return new(job.State == RequirementProcessingJobState.Running ? "Running"
            : job.DispatchAdmittedAtUtc is not null ? "Admitted" : "Waiting", true, false);
    }
}
