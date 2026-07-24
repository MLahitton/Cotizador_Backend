namespace Contracts.PreQuotes;

public sealed record CreatePreQuoteDocumentResponse(
    Guid Id,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);
