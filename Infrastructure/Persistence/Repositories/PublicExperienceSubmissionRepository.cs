using Application.PublicExperience;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class PublicExperienceSubmissionRepository(ApplicationDbContext context)
    : IPublicExperienceSubmissionRepository, IPublicExperienceProjectContextRepository
{
    public const string UniqueSubmissionIndex = "ux_public_experience_submissions_submission_id";

    public async Task<PublicExperienceProjectContext?> GetProjectContextByProjectIdAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var answers = await context.Set<PublicExperienceSubmission>().AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Source == "SNG_WEB")
            .Select(x => x.AnswersJson)
            .Take(2)
            .ToListAsync(cancellationToken);
        // Do not choose an arbitrary submission if multiple sources are linked.
        return answers.Count == 1 ? PublicExperienceProjectContext.FromAnswers(answers[0]) : null;
    }

    public Task<PublicExperienceSubmission?> FindAsync(string submissionId, CancellationToken cancellationToken) =>
        context.Set<PublicExperienceSubmission>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubmissionId == submissionId, cancellationToken);

    public async Task<bool> TryInsertAsync(PublicExperienceSubmission submission, CancellationToken cancellationToken)
    {
        context.Add(submission);
        try
        {
            // SaveChanges wraps the submission and all its file metadata in one transaction.
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueSubmissionIndex })
        {
            return false;
        }
        finally
        {
            foreach (var file in submission.Files.ToArray()) context.Entry(file).State = EntityState.Detached;
            context.Entry(submission).State = EntityState.Detached;
        }
    }
}
