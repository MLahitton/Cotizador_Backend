using System.Data.Common;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class DocumentProcessingRepository(
    ApplicationDbContext dbContext)
    : IDocumentProcessingRepository
{
    public async Task<DocumentProcessingSource?> FindDocumentSourceAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var query =
                from document in dbContext.PreQuoteDocuments.AsNoTracking()
                join preQuote in dbContext.PreQuotes
                    on document.PreQuoteId equals preQuote.Id
                join project in dbContext.Projects
                    on preQuote.ProjectId equals project.Id
                join client in dbContext.Clients
                    on project.ClientId equals client.Id
                where document.Id == documentId
                select new DocumentProcessingSource(
                    document.Id,
                    document.PreQuoteId,
                    document.OriginalFileName,
                    document.ContentType,
                    document.SizeBytes,
                    document.StorageKey,
                    project.Id,
                    project.IsActive,
                    client.Id,
                    client.IsActive);

            return await query.SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new DocumentProcessingQueryException(exception);
        }
    }

    public void AddAttempt(DocumentProcessingAttempt attempt)
    {
        dbContext.DocumentProcessingAttempts.Add(attempt);
    }

    public void AddResult(DocumentExtractionResult result)
    {
        dbContext.DocumentExtractionResults.Add(result);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new DocumentProcessingPersistenceException(exception);
        }
    }
}
