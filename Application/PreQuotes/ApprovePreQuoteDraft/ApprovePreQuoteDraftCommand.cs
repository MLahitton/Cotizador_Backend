namespace Application.PreQuotes.ApprovePreQuoteDraft;
public sealed record ApprovePreQuoteDraftCommand(Guid PreQuoteId, int ExpectedVersion);
