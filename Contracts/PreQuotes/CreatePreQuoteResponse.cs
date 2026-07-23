namespace Contracts.PreQuotes;

public sealed record CreatePreQuoteResponse(
    Guid Id,
    Guid ProjectId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
