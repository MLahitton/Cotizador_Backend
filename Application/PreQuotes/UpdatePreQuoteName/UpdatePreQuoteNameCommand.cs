namespace Application.PreQuotes.UpdatePreQuoteName;

public sealed record UpdatePreQuoteNameCommand(
    Guid PreQuoteId,
    string? Name);