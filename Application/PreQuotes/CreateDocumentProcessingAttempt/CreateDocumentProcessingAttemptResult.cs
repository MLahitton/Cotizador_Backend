using Application.PreQuotes;

namespace Application.PreQuotes.CreateDocumentProcessingAttempt;

public enum CreateDocumentProcessingAttemptFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    DocumentNotFound = 4,
    InactiveProject = 5,
    InactiveClient = 6,
    QueryError = 7,
    InitialPersistenceError = 8,
    FinalPersistenceError = 9,
    DocumentProcessingAlreadyActive = 10
}

public sealed record CreateDocumentProcessingAttemptResult(
    CreateDocumentProcessingAttemptFailure Failure,
    DocumentProcessingAttemptStatusData? Attempt)
{
    public bool IsSuccess =>
        Failure == CreateDocumentProcessingAttemptFailure.None
        && Attempt is not null;

    public static CreateDocumentProcessingAttemptResult Success(
        DocumentProcessingAttemptStatusData attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new CreateDocumentProcessingAttemptResult(
            CreateDocumentProcessingAttemptFailure.None,
            attempt);
    }

    public static CreateDocumentProcessingAttemptResult Failed(
        CreateDocumentProcessingAttemptFailure failure)
    {
        if (failure == CreateDocumentProcessingAttemptFailure.None)
        {
            throw new ArgumentException(
                "El failure de un resultado fallido no puede ser None.",
                nameof(failure));
        }

        return new CreateDocumentProcessingAttemptResult(failure, null);
    }
}
