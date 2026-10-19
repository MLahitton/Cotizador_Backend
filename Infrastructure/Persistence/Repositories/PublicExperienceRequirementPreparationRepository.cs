using Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Repositories;

public sealed class PublicExperienceRequirementPreparationRepository(
    ApplicationDbContext context, TimeProvider timeProvider,
    ILogger<PublicExperienceRequirementPreparationRepository> logger) : IPublicExperienceRequirementPreparationRepository
{
    public async Task<PublicExperienceRequirementPreparationResult?> PrepareLockedAsync(
        string submissionId, Func<PublicExperienceSubmission, CancellationToken, Task> prepare,
        Func<Task> compensate, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var submission = await context.Set<PublicExperienceSubmission>().FromSqlInterpolated(
            $"SELECT * FROM core.public_experience_submissions WHERE submission_id = {submissionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (submission is null) return null;
        await context.Entry(submission).ReloadAsync(cancellationToken);
        if (submission.PreQuoteId is not null && submission.RequirementId is not null)
            return Result(submission, true);
        if (submission.PreQuoteId is not null || submission.RequirementId is not null)
            return new(submission.SubmissionId, submission.PreQuoteId, submission.RequirementId,
                PublicExperienceRequirementPreparationErrors.InconsistentLinks);
        await context.Entry(submission).Collection(x => x.Files).LoadAsync(cancellationToken);
        var trackedBefore = context.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet();
        await transaction.CreateSavepointAsync("before_requirement_preparation", cancellationToken);
        try
        {
            await prepare(submission, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // No COMMIT was attempted. Rollback retains the submission lock for retry safety.
            try
            {
                await transaction.RollbackToSavepointAsync("before_requirement_preparation", CancellationToken.None);
                await context.Entry(submission).ReloadAsync(CancellationToken.None);
                var createdEntries = context.ChangeTracker.Entries()
                    .Where(entry => !trackedBefore.Contains(entry.Entity))
                    .OrderBy(entry => entry.Entity is RequirementFile ? 0 : entry.Entity is Requirement ? 1
                        : entry.Entity is PreQuote ? 2 : 3).ToArray();
                foreach (var entry in createdEntries) entry.State = EntityState.Detached;
            }
            finally { await compensate(); }
            if (cancellationToken.IsCancellationRequested) throw;
            var code = exception is PublicExperienceRequirementPreparationException expected
                ? expected.ErrorCode : PublicExperienceRequirementPreparationErrors.Failed;
            submission.FailRequirementPreparation(code, timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            logger.LogWarning(exception, "Public experience requirement preparation failed. SubmissionId={SubmissionId}", submissionId);
        }
        // An indeterminate commit must never delete files that may already be referenced.
        try { await transaction.CommitAsync(cancellationToken); }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Requirement preparation commit outcome unknown; copies retained for reconciliation. SubmissionId={SubmissionId}", submissionId);
            throw;
        }
        return Result(submission, false);
    }

    private static PublicExperienceRequirementPreparationResult Result(PublicExperienceSubmission submission, bool replay) =>
        new(submission.SubmissionId, submission.PreQuoteId, submission.RequirementId,
            submission.RequirementPreparationErrorCode, replay);
}
