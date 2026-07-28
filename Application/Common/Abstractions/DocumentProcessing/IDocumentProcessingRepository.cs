using Domain.PreQuotes;

namespace Application.Common.Abstractions.DocumentProcessing;

public interface IDocumentProcessingRepository
{
    Task<DocumentProcessingSource?> FindDocumentSourceAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<bool> HasActiveDocumentProcessingAttemptAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<Guid?> ClaimNextPendingDocumentProcessingAttemptAsync(
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken);

    Task<DocumentProcessingWorkItem?> FindProcessingWorkItemAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);

    Task<DocumentProcessingAttemptStatusSnapshot?> FindAttemptStatusAsync(
        Guid documentId,
        Guid processingAttemptId,
        Guid requestedByUserId,
        CancellationToken cancellationToken);

    void AddAttempt(DocumentProcessingAttempt attempt);

    void AddResult(DocumentExtractionResult result);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record DocumentProcessingWorkItem(
    DocumentProcessingAttempt Attempt,
    DocumentProcessingSource Source);

public sealed record DocumentProcessingAttemptStatusSnapshot(
    Guid ProcessingAttemptId,
    Guid DocumentId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome? Outcome,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultPayloadJson);

public sealed record DocumentProcessingSource(
    Guid DocumentId,
    Guid PreQuoteId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string StorageKey,
    Guid ProjectId,
    bool ProjectIsActive,
    Guid ClientId,
    bool ClientIsActive);

public sealed class DocumentProcessingQueryException : Exception
{
    public DocumentProcessingQueryException(Exception innerException)
        : base(
            "No fue posible consultar el documento para procesamiento.",
            innerException)
    {
    }
}

public sealed class DocumentProcessingPersistenceException : Exception
{
    public DocumentProcessingPersistenceException(Exception innerException)
        : base(
            "No fue posible guardar el intento de procesamiento.",
            innerException)
    {
    }
}

public sealed class DocumentProcessingActiveAttemptConflictException
    : Exception
{
    public DocumentProcessingActiveAttemptConflictException(
        Exception innerException)
        : base(
            "El documento ya tiene un intento de procesamiento activo.",
            innerException)
    {
    }
}
