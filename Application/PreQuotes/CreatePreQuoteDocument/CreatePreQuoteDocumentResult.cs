namespace Application.PreQuotes.CreatePreQuoteDocument;

public enum CreatePreQuoteDocumentFailure
{
    None = 0,
    InvalidRequest = 1,
    InvalidFileName = 2,
    UnsupportedFileType = 3,
    EmptyFile = 4,
    FileTooLarge = 5,
    Unauthorized = 6,
    InactiveUser = 7,
    PreQuoteNotFound = 8,
    ProjectNotFound = 9,
    InactiveProject = 10,
    ClientNotFound = 11,
    InactiveClient = 12,
    QueryError = 13,
    StorageError = 14,
    PersistenceError = 15,
    CompensationError = 16
}

public sealed record CreatedPreQuoteDocumentResult(
    Guid Id,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record CreatePreQuoteDocumentResult(
    bool IsSuccess,
    CreatedPreQuoteDocumentResult? Document,
    CreatePreQuoteDocumentFailure Failure)
{
    public static CreatePreQuoteDocumentResult Success(
        CreatedPreQuoteDocumentResult document)
    {
        return new CreatePreQuoteDocumentResult(
            true,
            document,
            CreatePreQuoteDocumentFailure.None);
    }

    public static CreatePreQuoteDocumentResult Failed(
        CreatePreQuoteDocumentFailure failure)
    {
        return new CreatePreQuoteDocumentResult(false, null, failure);
    }
}
