namespace Contracts.PreQuotes;

public static class DocumentErrorCodes
{
    public const string InvalidRequest = "DOCUMENT_INVALID_REQUEST";
    public const string UnsupportedFileType = "DOCUMENT_UNSUPPORTED_FILE_TYPE";
    public const string EmptyFile = "DOCUMENT_EMPTY_FILE";
    public const string FileTooLarge = "DOCUMENT_FILE_TOO_LARGE";
    public const string PreQuoteNotFound = "DOCUMENT_PREQUOTE_NOT_FOUND";
    public const string ProjectInactive = "DOCUMENT_PROJECT_INACTIVE";
    public const string ClientInactive = "DOCUMENT_CLIENT_INACTIVE";
    public const string StorageError = "DOCUMENT_STORAGE_ERROR";
    public const string PersistenceError = "DOCUMENT_PERSISTENCE_ERROR";
}
