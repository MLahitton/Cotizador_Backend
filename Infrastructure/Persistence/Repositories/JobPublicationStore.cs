using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class JobPublicationStore(DbContextOptions<ApplicationDbContext> options) : IJobPublicationStore
{
    private static JobPublicationResult Conflict(JobPublicationFailure reason) => new(JobPublicationState.Conflict, reason);
    private static JobPublicationResult Unknown() => new(JobPublicationState.Indeterminate, JobPublicationFailure.InconsistentEvidence);

    public async Task<JobPublicationResult> PublishAsync(AssignedJobExecution execution, Guid actorUserId,
        PreparedJobPublication snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null) return new(JobPublicationState.InvalidResult, JobPublicationFailure.InvalidRequest);
        if (execution is null || !execution.ProcessingAttemptId.HasValue || execution.ProcessingAttemptId == Guid.Empty)
            return Conflict(JobPublicationFailure.InvalidRequest);
        var commitAttempted = false;
        JobPublicationReceipt? candidate = null;
        try { return await RunAsync(execution, actorUserId, snapshot, false,
            receipt => { candidate = receipt; commitAttempted = true; }, cancellationToken); }
        catch (Exception) when (commitAttempted)
        {
            // RunAsync has disposed its failed scope. Observation never retries publication or the provider.
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            JobPublicationResult recognized;
            try
            {
                var result = await RunAsync(execution, actorUserId, snapshot, true, _ => { }, observation.Token);
                recognized = result.State == JobPublicationState.AlreadyPublished
                    && (snapshot.Evidence is null || candidate == result.Receipt) ? result : Unknown();
            }
            catch (Exception) { recognized = Unknown(); }
            if (snapshot.Evidence is not null) cancellationToken.ThrowIfCancellationRequested();
            return recognized;
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException) { return new(JobPublicationState.InvalidResult, JobPublicationFailure.InvalidRequest); }
        catch (Exception) { return new(JobPublicationState.PersistenceError, JobPublicationFailure.PersistenceError); }
    }

    private async Task<JobPublicationResult> RunAsync(AssignedJobExecution e, Guid actor, PreparedJobPublication snapshot,
        bool observeOnly, Action<JobPublicationReceipt> beforeCommit, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var route = await (from j in db.RequirementProcessingJobs.AsNoTracking()
            join r in db.Requirements.AsNoTracking() on j.RequirementId equals r.Id
            join q in db.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
            join p in db.Projects.AsNoTracking() on q.ProjectId equals p.Id
            where j.Id == e.JobId
            select new { j.SubmissionId, RequirementId = r.Id, QuoteId = q.Id, ProjectId = p.Id, p.ClientId }).SingleOrDefaultAsync(ct);
        // Same lock order as start/termination. Discovery alone never authorizes the operation.
        PublicExperienceSubmission? submission = null;
        if (route?.SubmissionId is Guid sid)
            submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
                $"SELECT * FROM core.public_experience_submissions WHERE id = {sid} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM identity.users WHERE id = {actor} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) return Conflict(JobPublicationFailure.Unauthorized);
        if (!user.IsActive) return Conflict(JobPublicationFailure.InactiveUser);
        if (route is null) return Conflict(JobPublicationFailure.NotFound);
        var client = await db.Set<Client>().FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var project = await db.Set<Project>().FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.CreatedByUserId != actor) return Conflict(JobPublicationFailure.NotFound);
        var quote = await db.PreQuotes.FromSqlInterpolated(
            $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var requirement = await db.Requirements.FromSqlInterpolated(
            $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var job = await db.RequirementProcessingJobs.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_jobs WHERE id = {e.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (client is null || job is null || requirement is null || quote is null || project.ClientId != route.ClientId
            || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id || job.RequirementId != requirement.Id
            || job.SubmissionId != route.SubmissionId)
            return Conflict(JobPublicationFailure.ContextConflict);
        if (job.ExecutionRequestId != e.ExecutionRequestId || job.RequirementId != e.RequirementId
            || job.ProcessingAttemptId != e.ProcessingAttemptId || job.LeaseGeneration != e.LeaseGeneration)
            return Conflict(JobPublicationFailure.IdentityConflict);
        if (job.SubmissionId.HasValue && (submission is null || submission.ClientId != client.Id
            || submission.ProjectId != project.Id || submission.PreQuoteId != quote.Id || submission.RequirementId != requirement.Id))
            return Conflict(JobPublicationFailure.ContextConflict);
        if (!ExperienceExecutionGuard.Matches(job, e.Permit, actor)
            || !ExperienceExecutionGuard.Assignment(e.Permit, e.LeaseOwner, e.LeaseGeneration))
            return Conflict(JobPublicationFailure.AuthorizationConflict);
        var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirement.Id} ORDER BY id FOR UPDATE").ToListAsync(ct);
        var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
        if (job.AttemptCount != 1 || attempt is null || job.StartedAtUtc is null || attempt.StartedAtUtc is null
            || attempt.StartedAtUtc < job.StartedAtUtc || attempt.CorrelationId != job.CorrelationId)
            return Conflict(JobPublicationFailure.AttemptConflict);
        var input = snapshot.ReadInput();
        if (input.Extraction.Payload.GetProperty("processingAttemptId").GetGuid() != attempt.Id)
            return Conflict(JobPublicationFailure.IdentityConflict);
        if (job.RequiredPublicationVersion != snapshot.ContractVersion)
            return Conflict(JobPublicationFailure.ResultMismatch);
        RequirementExecutionEvidence? evidence = null;
        if (snapshot.Evidence is { } expected)
        {
            if (expected.JobId != job.Id || expected.RequirementId != requirement.Id
                || expected.ExecutionRequestId != job.ExecutionRequestId || expected.ProcessingAttemptId != attempt.Id
                || expected.LeaseGeneration != job.LeaseGeneration || expected.ActorUserId != actor)
                return Conflict(JobPublicationFailure.IdentityConflict);
            evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files)
                .SingleOrDefaultAsync(x => x.JobId == job.Id, ct);
            try
            {
                if (evidence is null || PublicationExecutionEvidence.CanonicalJson(evidence) != PublicationExecutionEvidence.CanonicalJson(expected))
                    return Conflict(JobPublicationFailure.InconsistentEvidence);
            }
            catch (ArgumentException) { return Conflict(JobPublicationFailure.InconsistentEvidence); }
        }
        else if (snapshot.ContractVersion != PreparedJobPublication.Version)
            return Conflict(JobPublicationFailure.InconsistentEvidence);
        var extraction = await db.RequirementExtractionResults.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequirementProcessingAttemptId == attempt.Id, ct);
        var proposal = await db.RequirementTechnicalProposals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequirementProcessingAttemptId == attempt.Id, ct);
        if (job.State == RequirementProcessingJobState.Succeeded)
        {
            if (job.PublicationContractVersion is null || job.PublicationContentHash is null) return Unknown();
            if (job.PublicationContractVersion != snapshot.ContractVersion || job.PublicationContentHash != snapshot.ContentHash)
                return Conflict(JobPublicationFailure.ResultMismatch);
            if (extraction is null || proposal is null || proposal.RequirementId != requirement.Id
                || proposal.RequirementExtractionResultId != extraction.Id || job.FinishedAtUtc is null
                || attempt.ProcessingState != DocumentProcessingState.Finished || attempt.Outcome != input.Outcome
                || attempt.CompletedAtUtc != job.FinishedAtUtc || attempt.ErrorCode is not null || job.LastErrorCode is not null
                || job.CancellationRequestedAtUtc is not null || job.LeaseOwner is not null || job.LeaseExpiresAtUtc is not null
                || extraction.CreatedAtUtc != job.FinishedAtUtc || proposal.CreatedAtUtc != job.FinishedAtUtc)
                return Unknown();
            // No live lease/activity/current-Requirement or mutable commercial comparison for history.
            return Receipt(JobPublicationState.AlreadyPublished, job, extraction.Id, proposal.Id, input.Outcome);
        }
        if (job.State is RequirementProcessingJobState.Failed or RequirementProcessingJobState.Cancelled)
            return Conflict(JobPublicationFailure.TerminalConflict);
        if (observeOnly) return Unknown();
        if (!await ExperienceExecutionGuard.LiveAsync(db, job, ct))
            return Conflict(JobPublicationFailure.AuthorizationConflict);
        if (!project.IsActive) return Conflict(JobPublicationFailure.InactiveProject);
        if (!client.IsActive) return Conflict(JobPublicationFailure.InactiveClient);
        if (!requirement.IsCurrent || requirement.Status != RequirementStatus.Processing || job.State != RequirementProcessingJobState.Running)
            return Conflict(JobPublicationFailure.StateConflict);
        if (attempt.ProcessingState != DocumentProcessingState.Processing
            || attempts.Any(x => x.Id != attempt.Id && x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
            return Conflict(JobPublicationFailure.AttemptConflict);
        if (extraction is not null || proposal is not null || job.PublicationContentHash is not null || job.PublicationContractVersion is not null)
            return Conflict(JobPublicationFailure.InconsistentEvidence);
        if (job.CancellationRequestedAtUtc.HasValue) return Conflict(JobPublicationFailure.CancellationRequested);
        var files = await db.RequirementFiles.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_files WHERE requirement_id = {requirement.Id} ORDER BY created_at_utc, id FOR SHARE")
            .AsNoTracking().ToListAsync(ct);
        var manifest = files.Select(f => new PublicationFile(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.CreatedAtUtc)).ToArray();
        if (requirement.CommercialLine != input.CommercialLine || !manifest.SequenceEqual(input.Manifest)
            || manifest.Length == 0 || manifest[0].FileId != input.PrimaryDocumentId)
            return Conflict(JobPublicationFailure.ManifestMismatch);
        if (evidence is not null && (files.Count != evidence.FileCount || files.Where((f, i) =>
            f.Id != evidence.Files[i].FileId || f.StoredSha256 != evidence.Files[i].ExpectedSha256
            || f.StoredLengthBytes != evidence.Files[i].ExpectedLengthBytes
            || f.StoredEvidenceVersion != evidence.Files[i].StoredEvidenceVersion
            || f.StoredEvidenceAtUtc != evidence.Files[i].StoredEvidenceAtUtc).Any()))
            return Conflict(JobPublicationFailure.ManifestMismatch);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if (job.LeaseOwner != e.LeaseOwner || job.LeaseExpiresAtUtc is null || job.LeaseExpiresAtUtc <= now)
            return Conflict(JobPublicationFailure.LeaseConflict);
        var x = input.Extraction;
        extraction = RequirementExtractionResult.Create(attempt.Id, x.SchemaVersion, input.Provider.ToString(),
            x.Payload.GetRawText(), x.ItemCount, x.ItemsRequiringReview, x.IssueCount, x.ConflictCount, x.ProcessingMethod, x.DurationMs, now);
        var items = x.Items.Select(i => JobPublicationGraph.Materialize(i, extraction.Id, now)).ToDictionary(i => i.Sequence);
        proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, attempt.Id,
            input.Proposal.Status == RequirementTechnicalProposalStatus.RequiresReview, now);
        foreach (var i in input.Proposal.Items)
            proposal.AddItem(JobPublicationGraph.Materialize(i, proposal.Id, items[i.ExtractedItemSequence].Id, now));
        db.RequirementExtractionResults.Add(extraction);
        db.RequirementExtractedItems.AddRange(items.Values);
        db.RequirementTechnicalProposals.Add(proposal);
        attempt.Complete(input.Outcome, now);
        requirement.MarkProcessed(now);
        quote.RegisterActivity(now);
        job.SucceedWithPublicationReceipt(snapshot.ContractVersion, snapshot.ContentHash, now);
        await db.SaveChangesAsync(ct);
        var evidenceId = evidence?.Id;
        var manifestHash = evidence?.ManifestHash;
        // Detached job, old lease predicates, last write before commit. Never SaveChanges after this guard.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE core.requirement_processing_jobs
            SET state = 'Succeeded', finished_at_utc = {now}, lease_owner = NULL, lease_expires_at_utc = NULL,
                publication_contract_version = {snapshot.ContractVersion}, publication_content_hash = {snapshot.ContentHash}
            WHERE id = {e.JobId} AND execution_request_id = {e.ExecutionRequestId} AND requirement_id = {e.RequirementId}
                AND processing_attempt_id = {e.ProcessingAttemptId} AND attempt_count = 1
                AND state = 'Running' AND lease_owner = {e.LeaseOwner} AND lease_generation = {e.LeaseGeneration}
                AND cancellation_requested_at_utc IS NULL AND lease_expires_at_utc > clock_timestamp()
                AND publication_contract_version IS NULL AND publication_content_hash IS NULL
                AND required_publication_version = {snapshot.ContractVersion}
                AND automatic_authorization_policy_version IS NOT DISTINCT FROM {job.AutomaticAuthorizationPolicyVersion}
                AND authorized_actor_user_id IS NOT DISTINCT FROM {job.AuthorizedActorUserId}
                AND authorized_at_utc IS NOT DISTINCT FROM {job.AuthorizedAtUtc}
                AND authorization_expires_at_utc IS NOT DISTINCT FROM {job.AuthorizationExpiresAtUtc}
                AND dispatch_admitted_at_utc IS NOT DISTINCT FROM {job.DispatchAdmittedAtUtc}
                AND dispatch_instance_id IS NOT DISTINCT FROM {job.DispatchInstanceId}
                AND dispatch_claim_operation_id IS NOT DISTINCT FROM {job.DispatchClaimOperationId}
                AND (automatic_authorization_policy_version IS NULL OR (
                    authorization_revoked_at_utc IS NULL AND authorized_at_utc <= clock_timestamp()
                    AND clock_timestamp() < authorization_expires_at_utc))
                AND ({snapshot.ContractVersion} = 'sng-job-publication-v1' OR EXISTS (
                    SELECT 1 FROM core.requirement_execution_evidence ev
                    WHERE ev.id = {evidenceId} AND ev.job_id = {e.JobId}
                        AND ev.requirement_id = {e.RequirementId} AND ev.execution_request_id = {e.ExecutionRequestId}
                        AND ev.processing_attempt_id = {e.ProcessingAttemptId} AND ev.lease_generation = {e.LeaseGeneration}
                        AND ev.actor_user_id = {actor} AND ev.manifest_hash = {manifestHash} AND ev.manifest_version = 1))
            """, ct);
        if (affected != 1)
        {
            await tx.RollbackAsync(ct);
            return Conflict(JobPublicationFailure.LeaseConflict);
        }
        beforeCommit(Receipt(JobPublicationState.Published, job, extraction.Id, proposal.Id, input.Outcome).Receipt!);
        await tx.CommitAsync(ct);
        if (snapshot.Evidence is not null) ct.ThrowIfCancellationRequested();
        return Receipt(JobPublicationState.Published, job, extraction.Id, proposal.Id, input.Outcome);
    }

    private static JobPublicationResult Receipt(JobPublicationState state, RequirementProcessingJob job, Guid extraction,
        Guid proposal, DocumentProcessingOutcome outcome) => new(state, JobPublicationFailure.None,
        new(job.Id, job.ExecutionRequestId, job.RequirementId, job.ProcessingAttemptId!.Value, job.CorrelationId,
            extraction, proposal, outcome, job.FinishedAtUtc!.Value, job.PublicationContractVersion!, job.PublicationContentHash!));
}
