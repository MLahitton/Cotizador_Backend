namespace Contracts.PreQuotes;

public sealed record CreateRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string CommercialLine,
    string Status,
    DateTimeOffset CreatedAtUtc);
