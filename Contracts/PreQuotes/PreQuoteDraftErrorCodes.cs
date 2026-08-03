namespace Contracts.PreQuotes;

public static class PreQuoteDraftErrorCodes
{
    public const string InvalidRequest = "PREQUOTE_DRAFT_INVALID_REQUEST";
    public const string NotFound = "PREQUOTE_DRAFT_NOT_FOUND";
    public const string VersionConflict = "PREQUOTE_DRAFT_VERSION_CONFLICT";
    public const string ProjectInactive = "PREQUOTE_DRAFT_PROJECT_INACTIVE";
    public const string ClientInactive = "PREQUOTE_DRAFT_CLIENT_INACTIVE";
    public const string AlreadyExists = "PREQUOTE_DRAFT_ALREADY_EXISTS";
    public const string AlreadyApproved = "PREQUOTE_DRAFT_ALREADY_APPROVED";
    public const string PendingIssues = "PREQUOTE_DRAFT_PENDING_ISSUES";
    public const string PendingConflicts = "PREQUOTE_DRAFT_PENDING_CONFLICTS";
    public const string InvalidContent = "PREQUOTE_DRAFT_INVALID_CONTENT";
    public const string QueryError = "PREQUOTE_DRAFT_QUERY_ERROR";
    public const string PersistenceError = "PREQUOTE_DRAFT_PERSISTENCE_ERROR";
}
