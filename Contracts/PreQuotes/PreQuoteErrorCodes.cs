namespace Contracts.PreQuotes;

public static class PreQuoteErrorCodes
{
    public const string InvalidRequest = "PREQUOTE_INVALID_REQUEST";
    public const string Unauthorized = "AUTH_UNAUTHORIZED";
    public const string InactiveUser = "AUTH_USER_INACTIVE";
    public const string ProjectNotFound = "PREQUOTE_PROJECT_NOT_FOUND";
    public const string ProjectInactive = "PREQUOTE_PROJECT_INACTIVE";
    public const string ClientNotFound = "PREQUOTE_CLIENT_NOT_FOUND";
    public const string ClientInactive = "PREQUOTE_CLIENT_INACTIVE";
    public const string QueryError = "PREQUOTE_QUERY_ERROR";
    public const string PersistenceError = "PREQUOTE_PERSISTENCE_ERROR";
}
