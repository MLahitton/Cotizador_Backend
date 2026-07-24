namespace Contracts.PreQuotes;

public sealed record PreQuoteDetailsResponse(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
