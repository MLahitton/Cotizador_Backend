using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteStoredDocumentRepository(ApplicationDbContext dbContext)
    : IPreQuoteStoredDocumentRepository
{
    public async Task<StoredPreQuoteDocumentsReadModel?> GetForHistoricalEstimateAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var preQuote = await dbContext.PreQuotes
                .AsNoTracking()
                .Where(entity => entity.Id == preQuoteId
                    && entity.Project.CreatedByUserId == ownerUserId)
                .Select(entity => new { entity.Id, entity.ProjectId })
                .SingleOrDefaultAsync(cancellationToken);
            if (preQuote is null)
            {
                return null;
            }

            var query = dbContext.PreQuoteDocuments
                .AsNoTracking()
                .Where(document => document.PreQuoteId == preQuoteId);
            if (documentIds is not null)
            {
                query = query.Where(document => documentIds.Contains(document.Id));
            }

            var documents = await query
                .OrderBy(document => document.CreatedAtUtc)
                .ThenBy(document => document.Id)
                .Select(document => new StoredPreQuoteDocumentReadModel(
                    document.Id,
                    document.OriginalFileName,
                    document.ContentType,
                    document.SizeBytes,
                    document.StorageKey))
                .ToArrayAsync(cancellationToken);
            var expectedCount = documentIds?.Distinct().Count();

            return new StoredPreQuoteDocumentsReadModel(
                preQuote.Id,
                preQuote.ProjectId,
                expectedCount is null || documents.Length == expectedCount,
                documents);
        }
        catch (DbException exception)
        {
            throw new StoredPreQuoteDocumentQueryException(exception);
        }
    }
}
