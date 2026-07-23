namespace Contracts.PreQuotes;

public sealed record PreQuoteListItemResponse(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
