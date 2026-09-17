namespace Contracts.Search;

public sealed record GlobalSearchResponse(
    IReadOnlyList<GlobalSearchProjectResponse> Projects,
    IReadOnlyList<GlobalSearchPreQuoteResponse> PreQuotes,
    IReadOnlyList<GlobalSearchClientResponse> Clients);

public sealed record GlobalSearchProjectResponse(
    Guid ProjectId,
    string Code,
    string Name,
    string ClientName);

public sealed record GlobalSearchPreQuoteResponse(
    Guid PreQuoteId,
    string Serial,
    string? Name,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName);

public sealed record GlobalSearchClientResponse(
    Guid ClientId,
    string Name,
    string? DocumentType,
    string? DocumentNumber);