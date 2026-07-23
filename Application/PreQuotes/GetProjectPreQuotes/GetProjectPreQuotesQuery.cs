namespace Application.PreQuotes.GetProjectPreQuotes;

public sealed record GetProjectPreQuotesQuery(
    Guid ProjectId,
    int Page,
    int PageSize);
