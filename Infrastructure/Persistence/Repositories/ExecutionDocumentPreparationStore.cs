using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Application.PreQuotes.ProcessRequirement.ExecutionDocumentPreparationFailure;
using static Application.PreQuotes.ProcessRequirement.ExecutionDocumentPreparationState;

namespace Infrastructure.Persistence.Repositories;

// Explicit internal workflow: deliberately not registered in DI or called by intake/processing.
public sealed partial class ExecutionDocumentPreparationStore(DbContextOptions<ApplicationDbContext> options,
    IFileStorage storage, IVerifiedContentCopy verifier, ILogger<ExecutionDocumentPreparationStore> logger)
    : IExecutionDocumentConsumptionAuthorization, IExecutionDocumentPreparation
{
    public async Task<ExecutionDocumentPreparationResult> PrepareAsync(AssignedJobExecution execution, Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        ct.ThrowIfCancellationRequested();
        if (execution is null || actorUserId == Guid.Empty || execution.JobId == Guid.Empty
            || execution.RequirementId == Guid.Empty || execution.ExecutionRequestId == Guid.Empty
            || !execution.ProcessingAttemptId.HasValue || execution.ProcessingAttemptId == Guid.Empty
            || execution.LeaseGeneration != 1 || string.IsNullOrWhiteSpace(execution.LeaseOwner) || execution.LeaseOwner.Length > 200)
            return new(Rejected, InvalidRequest);
        var copies = new List<ICapturedContent>();
        var transferred = false;
        var commitAttempted = false;
        RequirementExecutionEvidence? candidate = null;
        try
        {
            Snapshot initial;
            await using (var db = new ApplicationDbContext(options))
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                initial = await ReadLockedAsync(db, execution, actorUserId, ct);
                ct.ThrowIfCancellationRequested();
                if (initial.Existing is not null) return Recognized(initial.Existing);
                await ValidateLiveAsync(db, initial, execution, ct);
            }

            try
            {
                foreach (var file in initial.Files)
                {
                    await using var source = await storage.OpenReadAsync(file.Key, ct);
                    var copy = await verifier.PrepareAsync(source, file.Length, file.Hash,
                        CreateRequirementService.MaximumFileSizeBytes, ct);
                    copies.Add(copy);
                    if (copy.Length != file.Length || copy.Sha256 != file.Hash) throw new ContentVerificationException();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (ContentVerificationException) { ct.ThrowIfCancellationRequested(); return new(Rejected, ContentMismatch); }
            catch (Exception) { ct.ThrowIfCancellationRequested(); return new(Rejected, SourceUnavailable); }

            DateTimeOffset verified;
            await using (var clock = new ApplicationDbContext(options)) verified = await Now(clock, ct);
            var id = Guid.NewGuid();
            ExecutionDocumentPreparationResult result;
            try
            {
                result = await RegisterAsync(initial, execution, actorUserId, id, verified, copies,
                    value => candidate = value, () => commitAttempted = true, ct);
            }
            catch (Exception) when (commitAttempted)
            {
                // The failed scope is disposed. Observation has its own budget, never the caller token.
                using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                result = new(Indeterminate, InconsistentEvidence);
                try
                {
                    await using var db = new ApplicationDbContext(options);
                    await using var tx = await db.Database.BeginTransactionAsync(observation.Token);
                    var observed = await ReadLockedAsync(db, execution, actorUserId, observation.Token);
                    if (candidate is not null && observed.Existing is { } found
                        && found.Id == candidate.Id && found.VerifiedAtUtc == candidate.VerifiedAtUtc
                        && found.RegisteredAtUtc == candidate.RegisteredAtUtc
                        && found.ManifestHash == candidate.ManifestHash && found.CanonicalManifest() == candidate.CanonicalManifest())
                        result = new(Registered, None, found);
                }
                catch (Exception) { /* Absence, denial or failed observation never proves rollback. */ }
                ct.ThrowIfCancellationRequested();
            }
            ct.ThrowIfCancellationRequested();
            if (result.State != Registered) return result;
            var package = new PreparedExecutionDocuments(result.Evidence!, copies, logger);
            transferred = true;
            return result with { Package = package, CommercialLine = initial.Requirement.CommercialLine };
        }
        catch (Denied error) { ct.ThrowIfCancellationRequested(); return new(error.Conflict ? Conflict : Rejected, error.Failure); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ct.ThrowIfCancellationRequested(); return new(Rejected, PersistenceError); }
        finally { if (!transferred) await PreparedExecutionDocuments.DisposeCopiesAsync(copies, logger); }
    }

    private async Task<ExecutionDocumentPreparationResult> RegisterAsync(Snapshot initial, AssignedJobExecution e,
        Guid actor, Guid id, DateTimeOffset verified, IReadOnlyList<ICapturedContent> copies,
        Action<RequirementExecutionEvidence> prepared, Action beforeCommit, CancellationToken ct)
    {
        await using var db = new ApplicationDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await ReadLockedAsync(db, e, actor, ct);
        if (!initial.SameSource(current)) throw new Denied(SnapshotChanged);
        if (current.Existing is not null) return Recognized(current.Existing);
        await ValidateLiveAsync(db, current, e, ct);
        var registered = await Now(db, ct);
        var evidence = MakeEvidence(current.Files, e, actor, id, verified, registered, copies);
        prepared(evidence);
        db.Add(evidence);
        await db.SaveChangesAsync(ct);
        // Detached job; no business value changes, no SaveChanges after this final fencing write.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE core.requirement_processing_jobs SET lease_generation = lease_generation
            WHERE id = {e.JobId} AND execution_request_id = {e.ExecutionRequestId} AND requirement_id = {e.RequirementId}
              AND processing_attempt_id = {e.ProcessingAttemptId} AND attempt_count = 1
              AND state = 'Running' AND lease_owner = {e.LeaseOwner} AND lease_generation = {e.LeaseGeneration}
              AND cancellation_requested_at_utc IS NULL AND lease_expires_at_utc > clock_timestamp()
              AND required_publication_version = 'sng-job-publication-v2'
              AND automatic_authorization_policy_version IS NOT DISTINCT FROM {current.Job.AutomaticAuthorizationPolicyVersion}
              AND authorized_actor_user_id IS NOT DISTINCT FROM {current.Job.AuthorizedActorUserId}
              AND authorized_at_utc IS NOT DISTINCT FROM {current.Job.AuthorizedAtUtc}
              AND authorization_expires_at_utc IS NOT DISTINCT FROM {current.Job.AuthorizationExpiresAtUtc}
              AND dispatch_admitted_at_utc IS NOT DISTINCT FROM {current.Job.DispatchAdmittedAtUtc}
              AND dispatch_instance_id IS NOT DISTINCT FROM {current.Job.DispatchInstanceId}
              AND dispatch_claim_operation_id IS NOT DISTINCT FROM {current.Job.DispatchClaimOperationId}
              AND (automatic_authorization_policy_version IS NULL OR (
                  authorization_revoked_at_utc IS NULL AND authorized_at_utc <= clock_timestamp()
                  AND clock_timestamp() < authorization_expires_at_utc))
            """, ct);
        if (affected != 1) throw new Denied(LeaseConflict);
        beforeCommit();
        await tx.CommitAsync(ct);
        return new(Registered, None, evidence);
    }

    private static ExecutionDocumentPreparationResult Recognized(RequirementExecutionEvidence evidence) => new(AlreadyRegistered, None, evidence);
    private static Task<DateTimeOffset> Now(ApplicationDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private sealed class Denied(ExecutionDocumentPreparationFailure failure, bool conflict = false) : Exception
    {
        internal ExecutionDocumentPreparationFailure Failure => failure;
        internal bool Conflict => conflict;
    }
    private sealed record File(Guid Id, string Name, string Mime, string Key, DateTimeOffset Created,
        long Declared, string Hash, long Length, DateTimeOffset Stored, short Version);
    private sealed record Snapshot(Guid? Submission, Guid ClientId, Guid ProjectId, Guid QuoteId,
        bool ClientActive, bool ProjectActive, Requirement Requirement, RequirementProcessingJob Job,
        RequirementProcessingAttempt Attempt, bool OtherActive, File[] Files, RequirementExecutionEvidence? Existing)
    {
        internal bool SameSource(Snapshot other) => Submission == other.Submission && ClientId == other.ClientId
            && ProjectId == other.ProjectId && QuoteId == other.QuoteId && Files.SequenceEqual(other.Files)
            && Job.CorrelationId == other.Job.CorrelationId && Job.StartedAtUtc == other.Job.StartedAtUtc
            && Attempt.StartedAtUtc == other.Attempt.StartedAtUtc;
    }

    private static async Task ValidateLiveAsync(ApplicationDbContext db, Snapshot s, AssignedJobExecution e, CancellationToken ct)
    {
        if (!await ExperienceExecutionGuard.LiveAsync(db, s.Job, ct)) throw new Denied(AuthorizationConflict);
        if (!s.ProjectActive) throw new Denied(InactiveProject);
        if (!s.ClientActive) throw new Denied(InactiveClient);
        if (!s.Requirement.IsCurrent || s.Requirement.Status != RequirementStatus.Processing
            || s.Job.State != RequirementProcessingJobState.Running) throw new Denied(StateConflict);
        if (s.Job.CancellationRequestedAtUtc.HasValue) throw new Denied(CancellationRequested);
        if (s.Attempt.ProcessingState != DocumentProcessingState.Processing || s.OtherActive) throw new Denied(AttemptConflict);
        if (s.Job.LeaseOwner != e.LeaseOwner || s.Job.LeaseExpiresAtUtc is null || s.Job.LeaseExpiresAtUtc <= await Now(db, ct))
            throw new Denied(LeaseConflict);
    }

    private static RequirementExecutionEvidence MakeEvidence(File[] files, AssignedJobExecution e, Guid actor, Guid id,
        DateTimeOffset verified, DateTimeOffset registered, IReadOnlyList<ICapturedContent>? copies = null) =>
        RequirementExecutionEvidence.Create(id, e.RequirementId, e.JobId, e.ExecutionRequestId, e.ProcessingAttemptId!.Value,
            e.LeaseGeneration, actor, files[0].Id, 1, verified, registered, files.Length,
            files.Select((f, i) => RequirementExecutionDocument.Create(id, f.Id, i, f.Name, f.Mime, f.Key, f.Created,
                f.Declared, f.Hash, f.Length, f.Stored, f.Version, copies is null ? f.Hash : copies[i].Sha256,
                copies is null ? f.Length : copies[i].Length)));

    private static async Task<Snapshot> ReadLockedAsync(ApplicationDbContext db, AssignedJobExecution e, Guid actor, CancellationToken ct)
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
        if (user is null) throw new Denied(Unauthorized);
        if (!user.IsActive) throw new Denied(InactiveUser);
        if (route is null) throw new Denied(NotFound);
        var client = await db.Set<Client>().FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var project = await db.Set<Project>().FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.CreatedByUserId != actor) throw new Denied(NotFound);
        var quote = await db.PreQuotes.FromSqlInterpolated(
            $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        var requirement = await db.Requirements.FromSqlInterpolated(
            $"SELECT * FROM core.requirements WHERE id = {route.RequirementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        var job = await db.RequirementProcessingJobs.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_jobs WHERE id = {e.JobId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (client is null || quote is null || requirement is null || job is null || project.ClientId != client.Id
            || quote.ProjectId != project.Id || requirement.PreQuoteId != quote.Id || job.RequirementId != requirement.Id
            || job.SubmissionId != route.SubmissionId) throw new Denied(ContextConflict);
        if (job.RequiredPublicationVersion != RequirementProcessingJob.PublicationV2) throw new Denied(StateConflict);
        if (job.RequirementId != e.RequirementId || job.ExecutionRequestId != e.ExecutionRequestId
            || job.ProcessingAttemptId != e.ProcessingAttemptId || job.LeaseGeneration != e.LeaseGeneration)
            throw new Denied(IdentityConflict);
        if (job.SubmissionId.HasValue && (submission is null || submission.RequirementId != requirement.Id
            || submission.PreQuoteId != quote.Id || submission.ProjectId != project.Id || submission.ClientId != client.Id))
            throw new Denied(ContextConflict);
        if (!ExperienceExecutionGuard.Matches(job, e.Permit, actor)
            || !ExperienceExecutionGuard.Assignment(e.Permit, e.LeaseOwner, e.LeaseGeneration))
            throw new Denied(AuthorizationConflict);
        var attempts = await db.RequirementProcessingAttempts.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirement.Id} ORDER BY id FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        var attempt = attempts.SingleOrDefault(x => x.Id == job.ProcessingAttemptId);
        if (attempt is null || job.AttemptCount != 1 || job.StartedAtUtc is null || attempt.StartedAtUtc is null
            || attempt.StartedAtUtc < job.StartedAtUtc || attempt.CorrelationId != job.CorrelationId) throw new Denied(AttemptConflict);
        var rows = await db.RequirementFiles.FromSqlInterpolated(
            $"SELECT * FROM core.requirement_files WHERE requirement_id = {requirement.Id} ORDER BY created_at_utc, id FOR SHARE")
            .AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0 || rows.Count > CreateRequirementService.MaximumFileCount
            || rows.Any(f => f.SizeBytes <= 0 || f.SizeBytes > CreateRequirementService.MaximumFileSizeBytes)
            || rows.Sum(f => f.SizeBytes) > CreateRequirementService.MaximumTotalSizeBytes) throw new Denied(InvalidDocuments);
        if (rows.Any(f => f.StoredEvidenceVersion != 1 || f.StoredSha256 is null || !f.StoredLengthBytes.HasValue
            || !f.StoredEvidenceAtUtc.HasValue)) throw new Denied(MissingIncorporation);
        var files = rows.Select(f => new File(f.Id, f.OriginalFileName, f.ContentType, f.StorageKey, f.CreatedAtUtc,
            f.SizeBytes, f.StoredSha256!, f.StoredLengthBytes!.Value, f.StoredEvidenceAtUtc!.Value, f.StoredEvidenceVersion!.Value)).ToArray();
        try { _ = MakeEvidence(files, e, actor, Guid.NewGuid(), files[0].Created, files[0].Created); }
        catch (ArgumentException) { throw new Denied(InvalidDocuments); }
        // Query by either unique identity to detect incompatible prior evidence as well as exact replay.
        var existing = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files)
            .Where(x => x.JobId == e.JobId || x.ProcessingAttemptId == e.ProcessingAttemptId).ToListAsync(ct);
        if (existing.Count > 1) throw new Denied(InconsistentEvidence, true);
        var found = existing.SingleOrDefault();
        if (found is not null)
        {
            try
            {
                var rebuilt = RequirementExecutionEvidence.Create(found.Id, found.RequirementId, found.JobId,
                    found.ExecutionRequestId, found.ProcessingAttemptId, found.LeaseGeneration, found.ActorUserId,
                    found.PrimaryDocumentId, found.ManifestVersion, found.VerifiedAtUtc, found.RegisteredAtUtc, found.FileCount, found.Files);
                var expected = MakeEvidence(files, e, actor, found.Id, found.VerifiedAtUtc, found.RegisteredAtUtc);
                if (found.ManifestHash != rebuilt.ManifestHash || rebuilt.CanonicalManifest() != expected.CanonicalManifest())
                    throw new Denied(InconsistentEvidence, true);
            }
            catch (ArgumentException) { throw new Denied(InconsistentEvidence, true); }
        }
        return new(job.SubmissionId, client.Id, project.Id, quote.Id, client.IsActive, project.IsActive,
            requirement, job, attempt, attempts.Any(x => x.Id != attempt.Id
                && x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing), files, found);
    }
}
