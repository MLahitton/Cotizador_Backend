using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IPreQuoteRepository
{
    Task<PreQuoteDetails?> FindByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken);

    Task<PreQuoteSearchPage> SearchByProjectAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void Add(PreQuote preQuote);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PreQuoteDetails(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PreQuoteSearchItem(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PreQuoteSearchPage(
    IReadOnlyList<PreQuoteSearchItem> Items,
    int TotalCount);

public sealed class PreQuoteQueryException : Exception
{
    public PreQuoteQueryException(Exception innerException)
        : base(
            "No fue posible consultar las precotizaciones.",
            innerException)
    {
    }
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
