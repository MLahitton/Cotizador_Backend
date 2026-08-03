namespace Contracts.PreQuotes;

public static class DocumentProcessingErrorCodes
{
    public const string InvalidRequest =
        "DOCUMENT_PROCESSING_INVALID_REQUEST";
    public const string DocumentNotFound =
        "PREQUOTE_DOCUMENT_NOT_FOUND";
    public const string ProjectInactive =
        "PREQUOTE_PROJECT_INACTIVE";
    public const string ClientInactive =
        "PREQUOTE_CLIENT_INACTIVE";
    public const string AlreadyActive =
        "DOCUMENT_PROCESSING_ALREADY_ACTIVE";
    public const string QueryError =
        "DOCUMENT_PROCESSING_QUERY_ERROR";
    public const string PersistenceError =
        "DOCUMENT_PROCESSING_PERSISTENCE_ERROR";
}
