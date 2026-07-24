using Domain.Identity;

namespace Domain.PreQuotes;

public sealed class DocumentProcessingAttempt
{
    private DocumentProcessingAttempt()
    {
    }

    private DocumentProcessingAttempt(
        Guid id,
        Guid preQuoteDocumentId,
        Guid requestedByUserId,
        Guid correlationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc,
        DocumentProcessingOutcome? outcome,
        string? errorCode)
    {
        Id = id;
        PreQuoteDocumentId = preQuoteDocumentId;
        RequestedByUserId = requestedByUserId;
        CorrelationId = correlationId;
        CreatedAtUtc = createdAtUtc;
        CompletedAtUtc = completedAtUtc;
        Outcome = outcome;
        ErrorCode = errorCode;
    }

    public Guid Id { get; private set; }

    public Guid PreQuoteDocumentId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DocumentProcessingOutcome? Outcome { get; private set; }

    public string? ErrorCode { get; private set; }

    public PreQuoteDocument PreQuoteDocument { get; private set; } = null!;

    public User RequestedByUser { get; private set; } = null!;

    public DocumentExtractionResult? ExtractionResult { get; private set; }

    public static DocumentProcessingAttempt Create(
        Guid preQuoteDocumentId,
        Guid requestedByUserId,
        Guid correlationId,
        DateTimeOffset createdAtUtc)
    {
        if (preQuoteDocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "El documento de precotización es obligatorio.",
                nameof(preQuoteDocumentId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario solicitante es obligatorio.",
                nameof(requestedByUserId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador de correlación es obligatorio.",
                nameof(correlationId));
        }

        return new DocumentProcessingAttempt(
            Guid.NewGuid(),
            preQuoteDocumentId,
            requestedByUserId,
            correlationId,
            createdAtUtc,
            null,
            null,
            null);
    }

    public void Complete(
        DocumentProcessingOutcome outcome,
        DateTimeOffset completedAtUtc)
    {
        if (outcome is not DocumentProcessingOutcome.Completed
            and not DocumentProcessingOutcome.RequiresReview)
        {
            throw new ArgumentException(
                "El resultado de procesamiento no es válido para completar el intento.",
                nameof(outcome));
        }

        EnsureNotFinalized();
        EnsureValidCompletionDate(completedAtUtc);

        Outcome = outcome;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = null;
    }

    public void Fail(
        string errorCode,
        DateTimeOffset completedAtUtc)
    {
        EnsureNotFinalized();
        EnsureValidCompletionDate(completedAtUtc);

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "El código de error es obligatorio.",
                nameof(errorCode));
        }

        var normalizedErrorCode = errorCode.Trim();

        if (normalizedErrorCode.Length > 64)
        {
            throw new ArgumentException(
                "El código de error no puede superar 64 caracteres.",
                nameof(errorCode));
        }

        Outcome = DocumentProcessingOutcome.Failed;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = normalizedErrorCode;
    }

    private void EnsureNotFinalized()
    {
        if (Outcome is not null || CompletedAtUtc is not null)
        {
            throw new InvalidOperationException(
                "El intento de procesamiento ya fue finalizado.");
        }
    }

    private void EnsureValidCompletionDate(
        DateTimeOffset completedAtUtc)
    {
        if (completedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de finalización no puede ser anterior a la fecha de creación.",
                nameof(completedAtUtc));
        }
    }
}
