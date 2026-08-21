namespace Contracts.PreQuotes;

public sealed record CreateRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string Status,
    DateTimeOffset CreatedAtUtc);
