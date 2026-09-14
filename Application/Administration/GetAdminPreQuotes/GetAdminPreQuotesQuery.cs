namespace Application.Administration.GetAdminPreQuotes;

public sealed record GetAdminPreQuotesQuery(
    string? Search,
    Guid? UserId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);