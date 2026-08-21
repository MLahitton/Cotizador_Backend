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
            return await BuildDocumentSourceQuery(documentId)
                .SingleOrDefaultAsync(cancellationToken);
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

    public async Task<DocumentProcessingWorkItem?> FindProcessingWorkItemAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        try
        {
            var attempt = await dbContext.DocumentProcessingAttempts
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == processingAttemptId,
                    cancellationToken);

            if (attempt is null)
            {
                return null;
            }

            var source = await BuildDocumentSourceQuery(
                    attempt.PreQuoteDocumentId)
                .SingleOrDefaultAsync(cancellationToken);

            return source is null
                ? null
                : new DocumentProcessingWorkItem(attempt, source);
        }
        catch (DbException exception)
        {
            throw new DocumentProcessingQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<DocumentProcessingSource>>
        ListDocumentSourcesByPreQuoteIdAsync(
            Guid preQuoteId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await (
                    from document in dbContext.PreQuoteDocuments.AsNoTracking()
                    join preQuote in dbContext.PreQuotes.AsNoTracking()
                        on document.PreQuoteId equals preQuote.Id
                    join project in dbContext.Projects.AsNoTracking()
                        on preQuote.ProjectId equals project.Id
                    join client in dbContext.Clients.AsNoTracking()
                        on project.ClientId equals client.Id
                    where document.PreQuoteId == preQuoteId
                    orderby document.CreatedAtUtc, document.Id
                    select new DocumentProcessingSource(
                        document.Id,
                        document.PreQuoteId,
                        document.OriginalFileName,
                        document.ContentType,
                        document.SizeBytes,
                        document.StorageKey,
                        project.Id,
                        project.CreatedByUserId,
                        project.IsActive,
                        client.Id,
                        client.IsActive))
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new DocumentProcessingQueryException(exception);
        }
    }

    public async Task<DocumentProcessingAttemptStatusSnapshot?>
        FindAttemptStatusAsync(
            Guid documentId,
            Guid processingAttemptId,
            Guid requestedByUserId,
            CancellationToken cancellationToken)
    {
        try
        {
            var query =
                from attempt in dbContext.DocumentProcessingAttempts
                    .AsNoTracking()
                join document in dbContext.PreQuoteDocuments.AsNoTracking()
                    on attempt.PreQuoteDocumentId equals document.Id
                join preQuote in dbContext.PreQuotes.AsNoTracking()
                    on document.PreQuoteId equals preQuote.Id
                join project in dbContext.Projects.AsNoTracking()
                    on preQuote.ProjectId equals project.Id
                join client in dbContext.Clients.AsNoTracking()
                    on project.ClientId equals client.Id
                where attempt.Id == processingAttemptId
                    && attempt.PreQuoteDocumentId == documentId
                    && attempt.RequestedByUserId == requestedByUserId
                    && project.IsActive
                    && client.IsActive
                select new DocumentProcessingAttemptStatusSnapshot(
                    attempt.Id,
                    attempt.PreQuoteDocumentId,
                    attempt.ProcessingState,
                    attempt.Outcome,
                    attempt.ErrorCode,
                    attempt.CreatedAtUtc,
                    attempt.StartedAtUtc,
                    attempt.CompletedAtUtc,
                    attempt.ExtractionResult == null
                        ? null
                        : attempt.ExtractionResult!.PayloadJson);

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

    public void AddStructuredExtraction(
        StructuredDocumentExtraction extraction)
    {
        dbContext.StructuredDocumentExtractions.Add(extraction);
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

    private IQueryable<DocumentProcessingSource> BuildDocumentSourceQuery(
        Guid documentId)
    {
        return
            from document in dbContext.PreQuoteDocuments.AsNoTracking()
            join preQuote in dbContext.PreQuotes.AsNoTracking()
                on document.PreQuoteId equals preQuote.Id
            join project in dbContext.Projects.AsNoTracking()
                on preQuote.ProjectId equals project.Id
            join client in dbContext.Clients.AsNoTracking()
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
                project.CreatedByUserId,
                project.IsActive,
                client.Id,
                client.IsActive);
    }
}
