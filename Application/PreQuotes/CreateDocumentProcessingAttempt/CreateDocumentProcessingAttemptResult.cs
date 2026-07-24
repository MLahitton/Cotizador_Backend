using Domain.PreQuotes;

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
    FinalPersistenceError = 9
}

public sealed record CreatedDocumentProcessingAttemptResult(
    Guid Id,
    Guid DocumentId,
    Guid CorrelationId,
    DocumentProcessingOutcome Outcome,
    string? ErrorCode,
    string? SchemaVersion,
    PdfClassification? Classification,
    bool? RequiresOcr,
    int? PageCount,
    int WarningCount,
    string? ProcessingMethod,
    int? DurationMs,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record CreateDocumentProcessingAttemptResult(
    CreateDocumentProcessingAttemptFailure Failure,
    CreatedDocumentProcessingAttemptResult? Attempt)
{
    public bool IsSuccess =>
        Failure == CreateDocumentProcessingAttemptFailure.None
        && Attempt is not null;

    public static CreateDocumentProcessingAttemptResult Success(
        CreatedDocumentProcessingAttemptResult attempt)
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
