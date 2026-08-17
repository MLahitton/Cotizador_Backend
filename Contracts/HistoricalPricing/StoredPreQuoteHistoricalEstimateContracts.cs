namespace Contracts.HistoricalPricing;

public sealed record StoredPreQuoteHistoricalEstimateRequest(
    IReadOnlyList<Guid>? DocumentIds = null);
