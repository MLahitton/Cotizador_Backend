using Application.PublicExperience;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class PublicExperienceProcessingRepository(
    ApplicationDbContext context, TimeProvider timeProvider,
    ILogger<PublicExperienceProcessingRepository> logger) : IPublicExperienceProcessingRepository
{
    public async Task<PublicExperienceProcessingResult?> ProcessLockedAsync(
        string submissionId, Func<PublicExperienceSubmission, CancellationToken, Task> process,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var submission = await context.Set<PublicExperienceSubmission>().FromSqlInterpolated(
            $"SELECT * FROM core.public_experience_submissions WHERE submission_id = {submissionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (submission is null) return null;
        // Refresh even when the scoped context had already tracked this row before acquiring the lock.
        await context.Entry(submission).ReloadAsync(cancellationToken);
        if (submission.Status == PublicExperienceSubmissionStatuses.Processed)
            return Result(submission, true);
        if (submission.Status == PublicExperienceSubmissionStatuses.Processing)
            return Result(submission, false);

        await context.Entry(submission).Collection(x => x.Files).LoadAsync(cancellationToken);

        var trackedBefore = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet();
        await transaction.CreateSavepointAsync("before_processing", cancellationToken);
        try
        {
            await process(submission, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the row lock acquired BEFORE this savepoint while undoing every new entity.
            await transaction.RollbackToSavepointAsync("before_processing", cancellationToken);
            await context.Entry(submission).ReloadAsync(cancellationToken);
            // Undo optional links first, then detach dependents before principals to avoid
            // EF relationship fixup severing the required Project -> Client -> User graph.
            var createdEntries = context.ChangeTracker.Entries()
                .Where(entry => !trackedBefore.Contains(entry.Entity))
                .OrderBy(entry => entry.Entity is Domain.Projects.Project ? 0
                    : entry.Entity is Domain.Clients.Client ? 1 : 2).ToArray();
            foreach (var entry in createdEntries) entry.State = EntityState.Detached;
            var errorCode = exception is DbUpdateException
                { InnerException: PostgresException
                    { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_projects_code" } }
                ? PublicExperienceProcessingErrors.ProjectCodeConflict
                : PublicExperienceProcessingErrors.ProcessingFailed;
            submission.FailProcessing(errorCode, timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            logger.LogError(exception, "Public experience processing failed. SubmissionId={SubmissionId}", submissionId);
        }
        await transaction.CommitAsync(cancellationToken);
        return Result(submission, false);
    }

    private static PublicExperienceProcessingResult Result(PublicExperienceSubmission submission, bool replay) =>
        new(submission.SubmissionId, submission.Status, submission.ClientId, submission.ProjectId,
            submission.ProcessingErrorCode, replay);
}
