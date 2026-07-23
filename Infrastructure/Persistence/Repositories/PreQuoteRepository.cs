using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteRepository(ApplicationDbContext dbContext)
    : IPreQuoteRepository
{
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
