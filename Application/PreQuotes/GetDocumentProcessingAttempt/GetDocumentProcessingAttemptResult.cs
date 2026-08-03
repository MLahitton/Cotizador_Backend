using Application.PreQuotes;

namespace Application.PreQuotes.GetDocumentProcessingAttempt;

public enum GetDocumentProcessingAttemptFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    DocumentNotFound = 4,
    AttemptNotFound = 5,
    QueryError = 6
}

public sealed record GetDocumentProcessingAttemptResult(
    GetDocumentProcessingAttemptFailure Failure,
    DocumentProcessingAttemptStatusData? Attempt)
{
    public bool IsSuccess =>
        Failure == GetDocumentProcessingAttemptFailure.None
        && Attempt is not null;

    public static GetDocumentProcessingAttemptResult Success(
        DocumentProcessingAttemptStatusData attempt)
    {
        return new GetDocumentProcessingAttemptResult(
            GetDocumentProcessingAttemptFailure.None,
            attempt);
    }

    public static GetDocumentProcessingAttemptResult Failed(
        GetDocumentProcessingAttemptFailure failure)
    {
        return new GetDocumentProcessingAttemptResult(failure, null);
    }
}
