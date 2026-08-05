using Application.Common.Abstractions.DocumentProcessing;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingDiagnostics(
    ILogger<DocumentProcessingDiagnostics> logger)
    : IDocumentProcessingDiagnostics
{
    public void ContractRejected(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        int? httpStatusCode,
        string stage,
        string category,
        int? itemSequence = null,
        string? rejectedNormalizedCode = null,
        IReadOnlyList<string>? acceptedNormalizedCodes = null,
        string? exceptionType = null,
        string? exceptionMessage = null,
        string? jsonPath = null,
        string? fieldName = null,
        string? rejectedValue = null)
    {
        var acceptedCodes = acceptedNormalizedCodes is null
            ? null
            : string.Join(
                ",",
                acceptedNormalizedCodes.OrderBy(
                    value => value,
                    StringComparer.Ordinal));

        logger.LogWarning(
            "Document processing response rejected. DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} HttpStatusCode={HttpStatusCode} Stage={Stage} Category={Category} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage} JsonPath={JsonPath} FieldName={FieldName} ItemSequence={ItemSequence} RejectedValue={RejectedValue} RejectedNormalizedCode={RejectedNormalizedCode} AcceptedNormalizedCodes={AcceptedNormalizedCodes}",
            documentId,
            processingAttemptId,
            correlationId,
            httpStatusCode,
            stage,
            category,
            exceptionType,
            exceptionMessage,
            jsonPath,
            fieldName,
            itemSequence,
            rejectedValue,
            rejectedNormalizedCode,
            acceptedCodes);
    }

    public void CatalogResolutionFailed(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        string category,
        string? normalizedCode,
        int? itemSequence = null,
        IReadOnlyList<string>? acceptedNormalizedCodes = null)
    {
        logger.LogWarning(
            "Glass catalog resolution failed. DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} Category={Category} ItemSequence={ItemSequence} RejectedNormalizedCode={RejectedNormalizedCode} AcceptedNormalizedCodes={AcceptedNormalizedCodes}",
            documentId,
            processingAttemptId,
            correlationId,
            category,
            itemSequence,
            normalizedCode,
            acceptedNormalizedCodes);
    }
}
