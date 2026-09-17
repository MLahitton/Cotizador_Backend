namespace Application.Common.Abstractions.Search;

public interface IGlobalSearchReader
{
    Task<GlobalSearchSnapshot> SearchAsync(
        Guid userId,
        bool isAdmin,
        string search,
        int limitPerCategory,
        CancellationToken cancellationToken);
}

public sealed record GlobalSearchSnapshot(
    IReadOnlyList<GlobalSearchProjectSnapshot> Projects,
    IReadOnlyList<GlobalSearchPreQuoteSnapshot> PreQuotes,
    IReadOnlyList<GlobalSearchClientSnapshot> Clients);

public sealed record GlobalSearchProjectSnapshot(
    Guid ProjectId,
    string Code,
    string Name,
    string ClientName);

public sealed record GlobalSearchPreQuoteSnapshot(
    Guid PreQuoteId,
    string Serial,
    string? Name,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName);

public sealed record GlobalSearchClientSnapshot(
    Guid ClientId,
    string Name,
    string? DocumentType,
    string? DocumentNumber);                                                            