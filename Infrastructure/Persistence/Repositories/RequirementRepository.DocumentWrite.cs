using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ManageRequirementDocuments;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence.Repositories;

public sealed partial class RequirementRepository
{
    public async Task<RequirementDocumentCapabilities> GetDocumentCapabilitiesAsync(
        Guid requirementId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var authorized = await (from r in dbContext.Requirements.AsNoTracking()
            join q in dbContext.PreQuotes on r.PreQuoteId equals q.Id
            join p in dbContext.Projects on q.ProjectId equals p.Id
            join c in dbContext.Set<Client>() on p.ClientId equals c.Id
            join u in dbContext.Users on actorUserId equals u.Id
            where r.Id == requirementId && p.CreatedByUserId == actorUserId
                && u.IsActive && p.IsActive && c.IsActive
            select r.Id).AnyAsync(cancellationToken);
        if (!authorized) return RequirementDocumentCapabilities.None;
        var requirement = await dbContext.Requirements.AsNoTracking()
            .SingleAsync(r => r.Id == requirementId, cancellationToken);
        var jobs = await dbContext.RequirementProcessingJobs.AsNoTracking()
            .Where(j => j.RequirementId == requirementId).ToListAsync(cancellationToken);
        var attempts = await dbContext.RequirementProcessingAttempts.AsNoTracking()
            .Where(a => a.RequirementId == requirementId).ToListAsync(cancellationToken);
        return RequirementDocumentPolicy.Evaluate(requirement, jobs, attempts, true);
    }

    public async Task<IRequirementDocumentWrite> BeginDocumentWriteAsync(
        Guid requirementId, Guid actorUserId, CancellationToken cancellationToken)
    {
        // Never discard pending work belonging to the caller.
        if (dbContext.ChangeTracker.HasChanges()) throw DocumentReject(ManageRequirementDocumentsFailure.PersistenceError);
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
            if (user is null) throw DocumentReject(ManageRequirementDocumentsFailure.Unauthorized);
            if (!user.IsActive) throw DocumentReject(ManageRequirementDocumentsFailure.InactiveUser);
            if (route is null) throw DocumentReject(ManageRequirementDocumentsFailure.RequirementNotFound);
            var client = await dbContext.Set<Client>().FromSqlInterpolated(
                $"SELECT * FROM core.clients WHERE id = {route.ClientId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var project = await dbContext.Set<Project>().FromSqlInterpolated(
                $"SELECT * FROM core.projects WHERE id = {route.ProjectId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (project is null || project.CreatedByUserId != actorUserId)
                throw DocumentReject(ManageRequirementDocumentsFailure.RequirementNotFound);
            if (!project.IsActive) throw DocumentReject(ManageRequirementDocumentsFailure.InactiveProject);
            if (client is null) throw DocumentReject(ManageRequirementDocumentsFailure.ClientNotFound);
            if (!client.IsActive) throw DocumentReject(ManageRequirementDocumentsFailure.InactiveClient);
            var quote = await dbContext.PreQuotes.FromSqlInterpolated(
                $"SELECT * FROM core.pre_quotes WHERE id = {route.QuoteId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            var requirement = await dbContext.Requirements.FromSqlInterpolated(
                $"SELECT * FROM core.requirements WHERE id = {requirementId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            if (quote is null || requirement is null || requirement.PreQuoteId != quote.Id
                || quote.ProjectId != project.Id || project.ClientId != route.ClientId)
                throw DocumentReject(ManageRequirementDocumentsFailure.RequirementNotFound);
            var jobs = await dbContext.RequirementProcessingJobs.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_jobs WHERE requirement_id = {requirementId} ORDER BY id FOR UPDATE")
                .AsNoTracking().ToListAsync(cancellationToken);
            var attempts = await dbContext.RequirementProcessingAttempts.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_processing_attempts WHERE requirement_id = {requirementId} ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken);
            var files = await dbContext.RequirementFiles.FromSqlInterpolated(
                $"SELECT * FROM core.requirement_files WHERE requirement_id = {requirementId} ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken);
            return new DocumentWrite(tx, requirement, quote, files,
                RequirementDocumentPolicy.Evaluate(requirement, jobs, attempts, true));
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private static RequirementDocumentRejectedException DocumentReject(ManageRequirementDocumentsFailure failure) => new(failure);

    private sealed class DocumentWrite(IDbContextTransaction transaction, Requirement requirement,
        PreQuote quote, IReadOnlyList<RequirementFile> files, RequirementDocumentCapabilities capabilities) : IRequirementDocumentWrite
    {
        public Requirement Requirement => requirement;
        public PreQuote PreQuote => quote;
        public IReadOnlyList<RequirementFile> Files => files;
        public RequirementDocumentCapabilities Capabilities => capabilities;
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
