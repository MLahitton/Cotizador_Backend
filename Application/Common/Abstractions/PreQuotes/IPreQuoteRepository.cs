using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IPreQuoteRepository
{
    void Add(PreQuote preQuote);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class PreQuotePersistenceException : Exception
{
    public PreQuotePersistenceException(Exception innerException)
        : base(
            "No fue posible guardar la precotización.",
            innerException)
    {
    }
}
