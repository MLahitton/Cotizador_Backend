namespace Application.PreQuotes.CreatePreQuoteDraft;
public sealed record CreatePreQuoteDraftCommand(
    Guid PreQuoteId,
    Guid SourceDocumentId,
    Guid SourceStructuredExtractionId);
