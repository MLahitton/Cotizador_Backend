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
        DocumentProcessingState processingState,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        DocumentProcessingOutcome? outcome,
        string? errorCode)
    {
        Id = id;
        PreQuoteDocumentId = preQuoteDocumentId;
        RequestedByUserId = requestedByUserId;
        CorrelationId = correlationId;
        CreatedAtUtc = createdAtUtc;
        ProcessingState = processingState;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Outcome = outcome;
        ErrorCode = errorCode;
    }

    public Guid Id { get; private set; }

    public Guid PreQuoteDocumentId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DocumentProcessingState ProcessingState { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

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

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new DocumentProcessingAttempt(
            Guid.NewGuid(),
            preQuoteDocumentId,
            requestedByUserId,
            correlationId,
            createdAtUtc,
            DocumentProcessingState.Pending,
            null,
            null,
            null,
            null);
    }

    public void Start(DateTimeOffset startedAtUtc)
    {
        if (ProcessingState != DocumentProcessingState.Pending)
        {
            throw new InvalidOperationException(
                "El intento de procesamiento no se encuentra pendiente.");
        }

        EnsureUtc(startedAtUtc, nameof(startedAtUtc));

        if (startedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de inicio no puede ser anterior a la fecha de creación.",
                nameof(startedAtUtc));
        }

        ProcessingState = DocumentProcessingState.Processing;
        StartedAtUtc = startedAtUtc;
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

        EnsureProcessing();
        EnsureValidCompletionDate(completedAtUtc);

        ProcessingState = DocumentProcessingState.Finished;
        Outcome = outcome;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = null;
    }

    public void Fail(
        string errorCode,
        DateTimeOffset completedAtUtc)
    {
        EnsureProcessing();
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

        ProcessingState = DocumentProcessingState.Finished;
        Outcome = DocumentProcessingOutcome.Failed;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = normalizedErrorCode;
    }

    private void EnsureProcessing()
    {
        if (ProcessingState != DocumentProcessingState.Processing
            || StartedAtUtc is null)
        {
            throw new InvalidOperationException(
                "El intento de procesamiento no se encuentra en procesamiento.");
        }
    }

    private void EnsureValidCompletionDate(
        DateTimeOffset completedAtUtc)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));

        if (StartedAtUtc is not { } startedAtUtc
            || completedAtUtc < startedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de finalización no puede ser anterior a la fecha de inicio.",
                nameof(completedAtUtc));
        }
    }

    private static void EnsureUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "La fecha debe expresarse en UTC.",
                parameterName);
        }
    }
}
