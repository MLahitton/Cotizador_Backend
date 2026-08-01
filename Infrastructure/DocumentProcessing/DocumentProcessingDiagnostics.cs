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
        string category)
    {
        logger.LogWarning(
            "Document processing response rejected. DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} HttpStatusCode={HttpStatusCode} Stage={Stage} Category={Category}",
            documentId,
            processingAttemptId,
            correlationId,
            httpStatusCode,
            stage,
            category);
    }

    public void CatalogResolutionFailed(
        Guid documentId,
        Guid processingAttemptId,
        Guid correlationId,
        string category,
        string? normalizedCode)
    {
        logger.LogWarning(
            "Glass catalog resolution failed. DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} Category={Category} NormalizedCode={NormalizedCode}",
            documentId,
            processingAttemptId,
            correlationId,
            category,
            normalizedCode);
    }
}
