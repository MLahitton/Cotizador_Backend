using Domain.PreQuotes;

namespace Application.Common.Abstractions.DocumentProcessing;

public interface IDocumentProcessingClient
{
    Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken);
}

public sealed record DocumentProcessingClientRequest(
    Guid DocumentId,
    Guid ProcessingAttemptId,
    Guid CorrelationId,
    string FileName,
    long SizeBytes,
    Stream Content);

public enum DocumentProcessingClientFailure
{
    None = 0,
    RemoteRejection = 1,
    ServiceUnavailable = 2,
    Timeout = 3,
    InvalidResponse = 4,
    ServiceError = 5
}

public sealed record DocumentProcessingRemoteError(
    int StatusCode,
    string SchemaVersion,
    string ErrorCode,
    string Message);

public sealed record ProcessedDocumentData(
    string FileName,
    string ContentType,
    long SizeBytes,
    int PageCount,
    PdfClassification Classification,
    bool RequiresOcr);

public sealed record ProcessedPageData(
    int PageNumber,
    string Text,
    int CharacterCount,
    bool HasExtractableText);

public sealed record ProcessingWarningData(
    string Code,
    string Message,
    IReadOnlyList<int> PageNumbers);

public sealed record ProcessingMetadataData(
    string Method,
    int DurationMs);

public sealed record DocumentProcessingResponseData(
    string SchemaVersion,
    Guid DocumentId,
    Guid ProcessingAttemptId,
    DocumentProcessingOutcome Outcome,
    ProcessedDocumentData Document,
    IReadOnlyList<ProcessedPageData> Pages,
    IReadOnlyList<ProcessingWarningData> Warnings,
    ProcessingMetadataData ProcessingMetadata,
    string PayloadJson);

public sealed record DocumentProcessingClientResult(
    DocumentProcessingClientFailure Failure,
    DocumentProcessingResponseData? Response,
    DocumentProcessingRemoteError? RemoteError)
{
    public bool IsSuccess =>
        Failure == DocumentProcessingClientFailure.None
        && Response is not null
        && RemoteError is null;

    public static DocumentProcessingClientResult Success(
        DocumentProcessingResponseData response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new DocumentProcessingClientResult(
            DocumentProcessingClientFailure.None,
            response,
            null);
    }

    public static DocumentProcessingClientResult Failed(
        DocumentProcessingClientFailure failure)
    {
        if (failure is DocumentProcessingClientFailure.None
            or DocumentProcessingClientFailure.RemoteRejection
            or DocumentProcessingClientFailure.ServiceError)
        {
            throw new ArgumentException(
                "El failure indicado no es válido para un resultado sin error remoto.",
                nameof(failure));
        }

        return new DocumentProcessingClientResult(failure, null, null);
    }

    public static DocumentProcessingClientResult RemoteFailure(
        DocumentProcessingClientFailure failure,
        DocumentProcessingRemoteError remoteError)
    {
        if (failure is not DocumentProcessingClientFailure.RemoteRejection
            and not DocumentProcessingClientFailure.ServiceError)
        {
            throw new ArgumentException(
                "El failure indicado no admite un error remoto.",
                nameof(failure));
        }

        ArgumentNullException.ThrowIfNull(remoteError);

        return new DocumentProcessingClientResult(
            failure,
            null,
            remoteError);
    }
}
