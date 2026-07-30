namespace Application.PreQuotes;

public enum PreQuoteDraftFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    NotFound,
    InactiveProject,
    InactiveClient,
    DraftAlreadyExists,
    DraftAlreadyApproved,
    VersionConflict,
    InvalidDraftContent,
    PendingIssues,
    PendingConflicts,
    QueryError,
    PersistenceError
}
