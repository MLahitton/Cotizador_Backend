using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteRepository(ApplicationDbContext dbContext)
    : IPreQuoteRepository
{
    public async Task<PreQuoteDetails?> FindByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.PreQuotes
                .AsNoTracking()
                .Where(preQuote => preQuote.Id == preQuoteId)
                .Select(preQuote => new PreQuoteDetails(
                    preQuote.Id,
                    preQuote.ProjectId,
                    dbContext.PreQuoteDocuments.Count(document =>
                        document.PreQuoteId == preQuote.Id),
                    preQuote.CreatedAtUtc,
                    preQuote.UpdatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteQueryException(exception);
        }
    }

    public async Task<PreQuoteSearchPage> SearchByProjectAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.PreQuotes
                .AsNoTracking()
                .Where(preQuote => preQuote.ProjectId == projectId);

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)page - 1L) * pageSize;

            if (totalCount == 0
                || skip >= totalCount
                || skip > int.MaxValue)
            {
                return new PreQuoteSearchPage(
                    Array.Empty<PreQuoteSearchItem>(),
                    totalCount);
            }

            var items = await query
                .OrderByDescending(
                    preQuote => preQuote.UpdatedAtUtc)
                .ThenByDescending(
                    preQuote => preQuote.CreatedAtUtc)
                .ThenByDescending(preQuote => preQuote.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(preQuote => new PreQuoteSearchItem(
                    preQuote.Id,
                    preQuote.ProjectId,
                    dbContext.PreQuoteDocuments.Count(document =>
                        document.PreQuoteId == preQuote.Id),
                    preQuote.CreatedAtUtc,
                    preQuote.UpdatedAtUtc))
                .ToListAsync(cancellationToken);

            return new PreQuoteSearchPage(items, totalCount);
        }
        catch (DbException exception)
        {
            throw new PreQuoteQueryException(exception);
        }
    }

    public void Add(PreQuote preQuote)
    {
        dbContext.PreQuotes.Add(preQuote);
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
            throw new PreQuotePersistenceException(exception);
        }
    }
}
