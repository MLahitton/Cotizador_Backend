namespace Contracts.PreQuotes;

public sealed record CreatePreQuoteResponse(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
