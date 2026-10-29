using Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Repositories;

public sealed partial class PublicExperienceRequirementPreparationRepository(
    ApplicationDbContext context, TimeProvider timeProvider,
    ILogger<PublicExperienceRequirementPreparationRepository> logger,
    IOptions<PublicExperienceOptions>? options = null) : IPublicExperienceRequirementPreparationRepository
{
    private readonly bool emitAutomaticExecution = options?.Value.EmitAutomaticExecution ?? false;
    // Dedicated tracking and connection for every phase, including reconciliation.
    private ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>(
        (DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>())
        .UseNpgsql(context.Database.GetConnectionString()).Options);

    public async Task<PublicPreparationRead> ReadAsync(string submissionId, CancellationToken cancellationToken)
    {
        await using var db = NewContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var state = await LoadAsync(db, submissionId, cancellationToken);
        return state is null ? new(null, null) : new(state.Snapshot, state.Result);
    }

    public Task<PublicExperienceRequirementPreparationResult> CommitAsync(PublicPreparationSnapshot snapshot,
        Requirement candidate, IReadOnlyList<PublicPreparedFile> files, CancellationToken cancellationToken) =>
        WriteAsync(snapshot, candidate, files, null, cancellationToken);

    public Task<PublicExperienceRequirementPreparationResult> FailAsync(PublicPreparationSnapshot snapshot,
        string errorCode, CancellationToken cancellationToken) =>
        WriteAsync(snapshot, null, [], errorCode, cancellationToken);

    private async Task<PublicExperienceRequirementPreparationResult> WriteAsync(PublicPreparationSnapshot snapshot,
        Requirement? candidate, IReadOnlyList<PublicPreparedFile> files, string? errorCode, CancellationToken ct)
    {
        var commitAttempted = false;
        RequirementProcessingJob? issued = null;
        try
        {
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var state = await LoadAsync(db, snapshot.SubmissionId, ct);
            if (state is null) return Rejected(snapshot);
            // A winner's physical keys need not match this invocation's candidate.
            if (state.Result is not null) return state.Result;
            if (!snapshot.Matches(state.Snapshot)) return Rejected(snapshot);
            if (candidate is not null)
            {
                if (state.Snapshot.ErrorCode is not null) return Rejected(snapshot, state.Snapshot.ErrorCode);
                ValidateCandidate(snapshot, candidate, files);
                if (emitAutomaticExecution)
                {
                    if (snapshot.Status != PublicExperienceSubmissionStatuses.Processed
                        || snapshot.Files.Select(f => f.Sequence).Distinct().Count() != snapshot.Files.Count
                        || snapshot.Files.Any(f => f.StoredEvidenceVersion != 1 || f.StoredEvidenceAtUtc is null
                            || f.StoredLengthBytes != f.SizeBytes || string.IsNullOrWhiteSpace(f.StorageKey)))
                        return Rejected(snapshot, PublicExperienceRequirementPreparationErrors.FileIntegrity);
                    if (await db.RequirementProcessingJobs.AnyAsync(j => j.SubmissionId == snapshot.Id
                        || j.RequirementId == candidate.Id, ct)
                        || await db.RequirementProcessingAttempts.AnyAsync(a => a.RequirementId == candidate.Id, ct))
                        return Rejected(snapshot, PublicExperienceRequirementPreparationErrors.InconsistentLinks);
                    var at = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
                    issued = RequirementProcessingJob.CreateForAutomaticExperience(Guid.NewGuid(), candidate.Id,
                        snapshot.Id, Guid.NewGuid(), Guid.NewGuid(), state.Snapshot.OwnerId!.Value, 1, at, at.AddHours(24));
                    db.RequirementProcessingJobs.Add(issued);
                }
                var serial = await new PreQuoteRepository(db).ReserveNextSerialAsync(candidate.CreatedAtUtc, ct);
                var quote = PreQuote.Create(snapshot.ProjectId!.Value, snapshot.OwnerId!.Value, serial,
                    null, candidate.CreatedAtUtc, candidate.PreQuoteId);
                db.PreQuotes.Add(quote);
                db.Requirements.Add(candidate);
                for (var i = 0; i < files.Count; i++)
                {
                    var source = snapshot.Files[i];
                    var file = files[i];
                    db.RequirementFiles.Add(RequirementFile.CreateWithStoredEvidence(candidate.Id,
                        source.OriginalFileName, source.ContentType, source.SizeBytes, file.StorageKey,
                        candidate.CreatedAtUtc.AddTicks(i * 10L), file.Receipt.Sha256, file.Receipt.Length,
                        file.EvidenceAtUtc, 1));
                }
                state.Submission.CompleteRequirementPreparation(quote.Id, candidate.Id, timeProvider.GetUtcNow());
            }
            else
            {
                state.Submission.FailRequirementPreparation(errorCode!, timeProvider.GetUtcNow());
            }
            await db.SaveChangesAsync(ct);
            ct.ThrowIfCancellationRequested();
            commitAttempted = true;
            await tx.CommitAsync(ct);
            return Result(state.Submission, false);
        }
        catch (Exception exception)
        {
            if (commitAttempted)
            {
                // All uncertain scopes have been disposed before opening the observer.
                var observed = await ObserveAsync(snapshot, candidate, files, issued);
                ct.ThrowIfCancellationRequested();
                if (observed is not null) return observed;
                logger.LogWarning("Public preparation commit could not be demonstrated. SubmissionId={SubmissionId}", snapshot.SubmissionId);
                throw;
            }
            ct.ThrowIfCancellationRequested();
            if (candidate is null) throw;
            logger.LogWarning(exception, "Public preparation rolled back before commit. SubmissionId={SubmissionId}", snapshot.SubmissionId);
            return await FailAsync(snapshot, PublicExperienceRequirementPreparationErrors.Failed, ct);
        }
        finally { ct.ThrowIfCancellationRequested(); }
    }

    private async Task<PublicExperienceRequirementPreparationResult?> ObserveAsync(PublicPreparationSnapshot snapshot,
        Requirement? candidate, IReadOnlyList<PublicPreparedFile> files, RequirementProcessingJob? issued)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync(budget.Token);
            var state = await LoadAsync(db, snapshot.SubmissionId, budget.Token);
            if (state?.Result is not { IsSuccess: true } result || !snapshot.Matches(state.Snapshot)) return null;
            if (issued is not null && (result.RequirementId != candidate!.Id || state.AutomaticJob is null
                || !SameEmission(issued, state.AutomaticJob))) return null;
            var persisted = state.LinkedFiles;
            if (persisted.Count != snapshot.Files.Count) return null;
            for (var i = 0; i < persisted.Count; i++)
                if (!MatchesSource(persisted[i], snapshot.Files[i])) return null;
            if (candidate is not null && result.RequirementId == candidate.Id)
            {
                if (result.PreQuoteId != candidate.PreQuoteId || persisted.Count != files.Count) return null;
                for (var i = 0; i < files.Count; i++)
                    if (persisted[i].StorageKey != files[i].StorageKey
                        || persisted[i].CreatedAtUtc != candidate.CreatedAtUtc.AddTicks(i * 10L)
                        || persisted[i].StoredSha256 != files[i].Receipt.Sha256
                        || persisted[i].StoredLengthBytes != files[i].Receipt.Length
                        || persisted[i].StoredEvidenceVersion != 1
                        || persisted[i].StoredEvidenceAtUtc != Microseconds(files[i].EvidenceAtUtc)) return null;
                return result with { IsReplay = false };
            }
            // A concurrently committed winner must prove a complete matching incorporation.
            if (persisted.Count != snapshot.Files.Count) return null;
            for (var i = 0; i < persisted.Count; i++)
                if (persisted[i].StoredLengthBytes != snapshot.Files[i].SizeBytes
                    || persisted[i].StoredSha256 != snapshot.Files[i].ContentHash.ToUpperInvariant()
                    || persisted[i].StoredEvidenceVersion != 1) return null;
            return result;
        }
        catch (Exception)
        {
            // Read-only observation is bounded; absence or failure never proves rollback.
            return null;
        }
    }

    private static DateTimeOffset Microseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, TimeSpan.Zero);

    private static bool MatchesSource(RequirementFile actual, PublicPreparationFile source)
    {
        // Use the same domain normalization as insertion, including historical MIME/name whitespace.
        var expected = RequirementFile.Create(actual.RequirementId, source.OriginalFileName,
            source.ContentType, source.SizeBytes, actual.StorageKey, actual.CreatedAtUtc);
        return actual.OriginalFileName == expected.OriginalFileName && actual.ContentType == expected.ContentType
            && actual.SizeBytes == expected.SizeBytes;
    }

    private static void ValidateCandidate(PublicPreparationSnapshot snapshot, Requirement candidate,
        IReadOnlyList<PublicPreparedFile> files)
    {
        if (candidate.CreatedByUserId != snapshot.OwnerId || candidate.Status != RequirementStatus.Pending || !candidate.IsCurrent
            || candidate.CommercialLine != PreparePublicExperienceRequirementService.CommercialLineBridge
            || files.Count == 0 || files.Count != snapshot.Files.Count
            || files.Select(f => f.StorageKey).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new InvalidOperationException("Invalid public preparation candidate.");
        for (var i = 0; i < files.Count; i++)
            if (files[i].SourceId != snapshot.Files[i].Id || files[i].Receipt.Length != snapshot.Files[i].SizeBytes
                || files[i].Receipt.Sha256 != snapshot.Files[i].ContentHash.ToUpperInvariant())
                throw new InvalidOperationException("Invalid public preparation receipt.");
    }

    private static async Task<State?> LoadAsync(ApplicationDbContext db, string id, CancellationToken ct)
    {
        var submission = await db.Set<PublicExperienceSubmission>().FromSqlInterpolated(
            $"SELECT * FROM core.public_experience_submissions WHERE submission_id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (submission is null) return null;
        var route = await db.Projects.AsNoTracking().Where(p => p.Id == submission.ProjectId)
            .Select(p => new { p.CreatedByUserId }).SingleOrDefaultAsync(ct);
        var ownerId = route?.CreatedByUserId ?? Guid.Empty;
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM identity.users WHERE id = {ownerId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var client = await db.Clients.FromSqlInterpolated(
            $"SELECT * FROM core.clients WHERE id = {submission.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var project = await db.Projects.FromSqlInterpolated(
            $"SELECT * FROM core.projects WHERE id = {submission.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
        var contextValid = user is not null && client is not null && project is not null
            && project.ClientId == client.Id && project.CreatedByUserId == ownerId;
        var error = submission.ClientId is null || submission.ProjectId is null
            ? PublicExperienceRequirementPreparationErrors.NotReady
            : !contextValid || !user!.IsActive || !client!.IsActive || !project!.IsActive
                ? PublicExperienceRequirementPreparationErrors.InvalidProject : null;
        PreQuote? quote = null;
        Requirement? requirement = null;
        List<RequirementFile> linkedFiles = [];
        List<RequirementProcessingJob> jobs = [];
        if (submission.PreQuoteId is not null && submission.RequirementId is not null)
        {
            quote = await db.PreQuotes.FromSqlInterpolated(
                $"SELECT * FROM core.pre_quotes WHERE id = {submission.PreQuoteId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            requirement = await db.Requirements.FromSqlInterpolated(
                $"SELECT * FROM core.requirements WHERE id = {submission.RequirementId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
            jobs = await db.RequirementProcessingJobs.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_jobs WHERE submission_id = {submission.Id} OR requirement_id = {submission.RequirementId} ORDER BY id FOR SHARE")
                .AsNoTracking().ToListAsync(ct);
            linkedFiles = await db.RequirementFiles.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_files WHERE requirement_id = {submission.RequirementId} ORDER BY id FOR SHARE")
                .AsNoTracking().ToListAsync(ct);
            linkedFiles = linkedFiles.OrderBy(f => f.CreatedAtUtc).ThenBy(f => f.Id).ToList();
        }
        var sources = await db.Set<PublicExperienceSubmissionFile>().FromSqlInterpolated(
            $"SELECT * FROM core.public_experience_submission_files WHERE submission_id = {submission.Id} ORDER BY id FOR SHARE")
            .AsNoTracking().ToListAsync(ct);
        var snapshot = new PublicPreparationSnapshot(submission.Id, submission.SubmissionId, submission.ClientId,
            submission.ProjectId, project?.CreatedByUserId, submission.Status, submission.PayloadFingerprint,
            submission.AnswersJson, sources.OrderBy(f => f.Sequence).Select(f => new PublicPreparationFile(f.Id,
                f.Sequence, f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.ContentHash,
                f.CreatedAtUtc, f.StoredLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion)).ToArray(), error);
        PublicExperienceRequirementPreparationResult? result = null;
        var automatic = jobs.Where(j => j.AutomaticAuthorizationPolicyVersion is not null).ToList();
        if (submission.PreQuoteId is not null || submission.RequirementId is not null)
        {
            var valid = contextValid && quote is not null && requirement is not null
                && quote.ProjectId == submission.ProjectId && quote.CreatedByUserId == ownerId
                && requirement.PreQuoteId == quote.Id && requirement.CreatedByUserId == ownerId
                && submission.RequirementPreparedAtUtc is not null && submission.RequirementPreparationErrorCode is null
                && linkedFiles.Count > 0 && automatic.Count <= 1
                && (automatic.Count == 0 || (ValidEmission(automatic[0], submission, requirement!, ownerId)
                    && MatchingEmissionDocuments(linkedFiles, snapshot.Files)));
            result = valid ? Result(submission, true) : Rejected(snapshot, PublicExperienceRequirementPreparationErrors.InconsistentLinks);
        }
        return new(submission, snapshot, result, linkedFiles, automatic.Count == 1 ? automatic[0] : null);
    }

    private static PublicExperienceRequirementPreparationResult Rejected(PublicPreparationSnapshot snapshot,
        string code = PublicExperienceRequirementPreparationErrors.Failed) =>
        new(snapshot.SubmissionId, null, null, code);

    private static PublicExperienceRequirementPreparationResult Result(PublicExperienceSubmission submission, bool replay) =>
        new(submission.SubmissionId, submission.PreQuoteId, submission.RequirementId,
            submission.RequirementPreparationErrorCode, replay);

    private sealed record State(PublicExperienceSubmission Submission, PublicPreparationSnapshot Snapshot,
        PublicExperienceRequirementPreparationResult? Result, List<RequirementFile> LinkedFiles,
        RequirementProcessingJob? AutomaticJob);
}
