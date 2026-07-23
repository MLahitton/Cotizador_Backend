namespace Contracts.PreQuotes;

public sealed record GetProjectPreQuotesResponse(
    IReadOnlyList<PreQuoteListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
