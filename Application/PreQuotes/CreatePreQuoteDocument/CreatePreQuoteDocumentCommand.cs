namespace Application.PreQuotes.CreatePreQuoteDocument;

public sealed record CreatePreQuoteDocumentCommand(
    Guid PreQuoteId,
    string? OriginalFileName,
    string? ContentType,
    long SizeBytes,
    Stream? Content);
