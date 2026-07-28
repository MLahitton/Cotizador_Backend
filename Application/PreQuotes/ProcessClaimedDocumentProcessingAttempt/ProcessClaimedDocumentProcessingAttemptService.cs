using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;

public enum ProcessClaimedDocumentProcessingAttemptResult
{
    Completed = 1,
    Failed = 2,
    NotFound = 3,
    InvalidState = 4,
    QueryError = 5,
    PersistenceError = 6
}

public interface IClaimedDocumentProcessingService
{
    Task<ProcessClaimedDocumentProcessingAttemptResult> ProcessAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);
}

public sealed class ProcessClaimedDocumentProcessingAttemptService(
    IDocumentProcessingRepository repository,
    IFileStorage fileStorage,
    IDocumentProcessingClient client,
    TimeProvider timeProvider)
    : IClaimedDocumentProcessingService
{
    private const string AiServiceUnavailableCode = "AI_SERVICE_UNAVAILABLE";
    private const string AiServiceTimeoutCode = "AI_SERVICE_TIMEOUT";
    private const string AiInvalidResponseCode = "AI_INVALID_RESPONSE";
    private const string AiServiceErrorCode = "AI_SERVICE_ERROR";
    private const string DocumentStorageErrorCode = "DOCUMENT_STORAGE_ERROR";

    public async Task<ProcessClaimedDocumentProcessingAttemptResult> ProcessAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        DocumentProcessingWorkItem? workItem;

        try
        {
            workItem = await repository.FindProcessingWorkItemAsync(
                processingAttemptId,
                cancellationToken);
        }
        catch (DocumentProcessingQueryException)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.QueryError;
        }

        if (workItem is null)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.NotFound;
        }

        if (workItem.Attempt.ProcessingState
            != DocumentProcessingState.Processing)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.InvalidState;
        }

        try
        {
            await using var content = await fileStorage.OpenReadAsync(
                workItem.Source.StorageKey,
                cancellationToken);
            var clientResult = await client.ProcessAsync(
                new DocumentProcessingClientRequest(
                    workItem.Source.DocumentId,
                    workItem.Attempt.Id,
                    workItem.Attempt.CorrelationId,
                    workItem.Source.OriginalFileName,
                    workItem.Source.SizeBytes,
                    content),
                cancellationToken);

            if (clientResult.IsSuccess
                && clientResult.Response is { } response)
            {
                return await CompleteAsync(
                    workItem.Attempt,
                    response,
                    cancellationToken);
            }

            return await FailAsync(
                workItem.Attempt,
                MapClientFailure(clientResult),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidStorageKeyException)
        {
            return await FailAsync(
                workItem.Attempt,
                DocumentStorageErrorCode,
                cancellationToken);
        }
        catch (FileStorageReadException)
        {
            return await FailAsync(
                workItem.Attempt,
                DocumentStorageErrorCode,
                cancellationToken);
        }
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult>
        CompleteAsync(
            DocumentProcessingAttempt attempt,
            DocumentProcessingResponseData response,
            CancellationToken cancellationToken)
    {
        var completedAtUtc = timeProvider.GetUtcNow();
        var extractionResult = DocumentExtractionResult.Create(
            attempt.Id,
            response.SchemaVersion,
            response.Document.Classification,
            response.Document.RequiresOcr,
            response.Document.PageCount,
            response.ProcessingMetadata.Method,
            response.ProcessingMetadata.DurationMs,
            response.PayloadJson,
            completedAtUtc);

        attempt.Complete(response.Outcome, completedAtUtc);
        repository.AddResult(extractionResult);

        return await SaveTerminalAsync(
            cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult.Completed);
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult> FailAsync(
        DocumentProcessingAttempt attempt,
        string errorCode,
        CancellationToken cancellationToken)
    {
        attempt.Fail(errorCode, timeProvider.GetUtcNow());

        return await SaveTerminalAsync(
            cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult.Failed);
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult>
        SaveTerminalAsync(
            CancellationToken cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult successResult)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return successResult;
        }
        catch (DocumentProcessingPersistenceException)
        {
            return ProcessClaimedDocumentProcessingAttemptResult
                .PersistenceError;
        }
    }

    private static string MapClientFailure(
        DocumentProcessingClientResult clientResult)
    {
        if (clientResult.Failure
                == DocumentProcessingClientFailure.RemoteRejection
            && clientResult.RemoteError is { } remoteError
            && IsRecognizedRemoteRejectionCode(remoteError.ErrorCode))
        {
            return remoteError.ErrorCode;
        }

        return clientResult.Failure switch
        {
            DocumentProcessingClientFailure.ServiceUnavailable =>
                AiServiceUnavailableCode,
            DocumentProcessingClientFailure.Timeout =>
                AiServiceTimeoutCode,
            DocumentProcessingClientFailure.ServiceError =>
                AiServiceErrorCode,
            _ => AiInvalidResponseCode
        };
    }

    private static bool IsRecognizedRemoteRejectionCode(
        string errorCode)
    {
        return errorCode is
            "INVALID_REQUEST"
            or "INVALID_CORRELATION_ID"
            or "EMPTY_FILE"
            or "INVALID_PDF"
            or "PDF_PASSWORD_REQUIRED"
            or "PDF_PAGE_LIMIT_EXCEEDED"
            or "FILE_TOO_LARGE"
            or "UNSUPPORTED_FILE_TYPE";
    }
}
