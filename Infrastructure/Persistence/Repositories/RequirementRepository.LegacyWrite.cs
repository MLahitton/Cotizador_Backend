using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence.Repositories;

public sealed partial class RequirementRepository
{
    public async Task<IRequirementLegacyWrite> BeginLegacyWriteAsync(
        Guid requirementId, Guid actorUserId, bool requireActiveContext, CancellationToken cancellationToken)
    {
        // No entity loaded before the locks may overwrite current operational state.
        dbContext.ChangeTracker.Clear();
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var route = await (from r in dbContext.Requirements.AsNoTracking()
                join q in dbContext.PreQuotes.AsNoTracking() on r.PreQuoteId equals q.Id
                join p in dbContext.Projects.AsNoTracking() on q.ProjectId equals p.Id
                where r.Id == requirementId
                select new { QuoteId = q.Id, ProjectId = p.Id, p.ClientId }).SingleOrDefaultAsync(cancellationToken);
            var user = await dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM identity.users WHERE id = {actorUserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (user is null) throw Reject(ProcessRequirementFailure.Unauthorized);
            if (!user.IsActive) throw Reject(ProcessRequirementFailure.InactiveUser);
            if (route is null) throw Reject(ProcessRequirementFailure.RequirementNotFound);
            var client = await dbContext.Set<Client>().FromSqlInterpolated(
                $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var project = await dbContext.Set<Project>().FromSqlInterpolated(
                $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (project is null || project.CreatedByUserId != actorUserId)
                throw Reject(ProcessRequirementFailure.RequirementNotFound);
            if (requireActiveContext && !project.IsActive) throw Reject(ProcessRequirementFailure.InactiveProject);
            if (requireActiveContext && (client is null || !client.IsActive)) throw Reject(ProcessRequirementFailure.InactiveClient);
            var quote = await dbContext.PreQuotes.FromSqlInterpolated(
                $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            var requirement = await dbContext.Requirements.FromSqlInterpolated(
                $"SELECT * FROM core.requirements WHERE id = {requirementId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            if (quote is null || requirement is null || requirement.PreQuoteId != quote.Id
                || quote.ProjectId != project.Id || project.ClientId != route.ClientId)
                throw Reject(ProcessRequirementFailure.RequirementNotFound);
            // The future enqueuer must take this same Requirement lock and reject active legacy attempts.
            if (await dbContext.RequirementProcessingJobs.AsNoTracking().AnyAsync(x => x.RequirementId == requirementId, cancellationToken))
                throw Reject(ProcessRequirementFailure.ProcessingManagedByJob);
            var attempts = await dbContext.RequirementProcessingAttempts.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirementId} ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken);
            return new LegacyWrite(tx, requirement, quote, attempts);
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private static LegacyProcessingRejectedException Reject(ProcessRequirementFailure failure) => new(failure);

    private sealed class LegacyWrite(IDbContextTransaction transaction, Requirement requirement,
        PreQuote quote, IReadOnlyList<RequirementProcessingAttempt> attempts) : IRequirementLegacyWrite
    {
        public Requirement Requirement => requirement;
        public PreQuote PreQuote => quote;
        public IReadOnlyList<RequirementProcessingAttempt> Attempts => attempts;
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            try { await transaction.CommitAsync(cancellationToken); }
            catch
            {
                // Unknown commit outcome: never launch a provider or another finalizer to compensate.
                throw Reject(ProcessRequirementFailure.PersistenceError);
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            catch { throw Reject(ProcessRequirementFailure.PersistenceError); }
        }
    }
}
