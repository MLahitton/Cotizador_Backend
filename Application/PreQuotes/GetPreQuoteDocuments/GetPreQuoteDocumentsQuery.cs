namespace Application.PreQuotes.GetPreQuoteDocuments;

public sealed record GetPreQuoteDocumentsQuery(
    Guid PreQuoteId,
    int Page,
    int PageSize);
