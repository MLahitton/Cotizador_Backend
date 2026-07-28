using System.Data.Common;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class DocumentProcessingRepository(
    ApplicationDbContext dbContext)
    : IDocumentProcessingRepository
{
    private const string ActiveAttemptIndexName =
        "ux_document_processing_attempts_active_pre_quote_document_id";

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

    public async Task<bool> HasActiveDocumentProcessingAttemptAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.DocumentProcessingAttempts
                .AsNoTracking()
                .AnyAsync(
                    attempt =>
                        attempt.PreQuoteDocumentId == documentId
                        && (attempt.ProcessingState
                                == DocumentProcessingState.Pending
                            || attempt.ProcessingState
                                == DocumentProcessingState.Processing),
                    cancellationToken);
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
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ActiveAttemptIndexName
            })
        {
            throw new DocumentProcessingActiveAttemptConflictException(
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new DocumentProcessingPersistenceException(exception);
        }
    }
}
