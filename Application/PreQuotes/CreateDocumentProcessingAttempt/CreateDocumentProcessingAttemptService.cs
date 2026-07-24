using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.CreateDocumentProcessingAttempt;

public sealed class CreateDocumentProcessingAttemptService(
    IValidator<CreateDocumentProcessingAttemptCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IDocumentProcessingRepository documentProcessingRepository,
    IFileStorage fileStorage,
    IDocumentProcessingClient documentProcessingClient)
{
    private const long MaximumPdfSizeBytes = 20 * 1024 * 1024;
    private const string ApplicationPdfContentType = "application/pdf";
    private const string AiServiceUnavailableCode = "AI_SERVICE_UNAVAILABLE";
    private const string AiServiceTimeoutCode = "AI_SERVICE_TIMEOUT";
    private const string AiInvalidResponseCode = "AI_INVALID_RESPONSE";
    private const string AiServiceErrorCode = "AI_SERVICE_ERROR";
    private const string DocumentStorageErrorCode = "DOCUMENT_STORAGE_ERROR";
    private const string RequestCancelledCode = "REQUEST_CANCELLED";

    public async Task<CreateDocumentProcessingAttemptResult> ExecuteAsync(
        CreateDocumentProcessingAttemptCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.InactiveUser);
        }

        DocumentProcessingSource? source;

        try
        {
            source = await documentProcessingRepository.FindDocumentSourceAsync(
                command.DocumentId,
                cancellationToken);
        }
        catch (DocumentProcessingQueryException)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.QueryError);
        }

        if (source is null)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.DocumentNotFound);
        }

        if (!source.ProjectIsActive)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.InactiveProject);
        }

        if (!source.ClientIsActive)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.InactiveClient);
        }

        if (!HasValidPersistedMetadata(source))
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.QueryError);
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        var attempt = DocumentProcessingAttempt.Create(
            source.DocumentId,
            userId,
            correlationId,
            createdAtUtc);

        try
        {
            documentProcessingRepository.AddAttempt(attempt);

            await documentProcessingRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (DocumentProcessingPersistenceException)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.InitialPersistenceError);
        }

        try
        {
            await using var content = await fileStorage.OpenReadAsync(
                source.StorageKey,
                cancellationToken);

            var clientResult = await documentProcessingClient.ProcessAsync(
                new DocumentProcessingClientRequest(
                    source.DocumentId,
                    attempt.Id,
                    attempt.CorrelationId,
                    source.OriginalFileName,
                    source.SizeBytes,
                    content),
                cancellationToken);

            if (clientResult.IsSuccess
                && clientResult.Response is { } response)
            {
                return await FinalizeSuccessfulAttemptAsync(
                    attempt,
                    source,
                    response);
            }

            return await FinalizeFailedAttemptAsync(
                attempt,
                source,
                MapClientFailure(clientResult));
        }
        catch (InvalidStorageKeyException)
        {
            return await FinalizeFailedAttemptAsync(
                attempt,
                source,
                DocumentStorageErrorCode);
        }
        catch (FileStorageReadException)
        {
            return await FinalizeFailedAttemptAsync(
                attempt,
                source,
                DocumentStorageErrorCode);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                attempt.Fail(
                    RequestCancelledCode,
                    DateTimeOffset.UtcNow);

                await documentProcessingRepository.SaveChangesAsync(
                    CancellationToken.None);
            }
            catch (DocumentProcessingPersistenceException)
            {
            }

            throw;
        }
    }

    private async Task<CreateDocumentProcessingAttemptResult>
        FinalizeSuccessfulAttemptAsync(
            DocumentProcessingAttempt attempt,
            DocumentProcessingSource source,
            DocumentProcessingResponseData response)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
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
        documentProcessingRepository.AddResult(extractionResult);

        try
        {
            await documentProcessingRepository.SaveChangesAsync(
                CancellationToken.None);
        }
        catch (DocumentProcessingPersistenceException)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.FinalPersistenceError);
        }

        if (attempt.Outcome is not { } outcome
            || attempt.CompletedAtUtc is not { } persistedCompletedAtUtc)
        {
            throw new InvalidOperationException(
                "El intento debe encontrarse finalizado.");
        }

        return CreateDocumentProcessingAttemptResult.Success(
            new CreatedDocumentProcessingAttemptResult(
                attempt.Id,
                source.DocumentId,
                attempt.CorrelationId,
                outcome,
                null,
                response.SchemaVersion,
                response.Document.Classification,
                response.Document.RequiresOcr,
                response.Document.PageCount,
                response.Warnings.Count,
                response.ProcessingMetadata.Method,
                response.ProcessingMetadata.DurationMs,
                attempt.CreatedAtUtc,
                persistedCompletedAtUtc));
    }

    private async Task<CreateDocumentProcessingAttemptResult>
        FinalizeFailedAttemptAsync(
            DocumentProcessingAttempt attempt,
            DocumentProcessingSource source,
            string errorCode)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;

        attempt.Fail(errorCode, completedAtUtc);

        try
        {
            await documentProcessingRepository.SaveChangesAsync(
                CancellationToken.None);
        }
        catch (DocumentProcessingPersistenceException)
        {
            return CreateDocumentProcessingAttemptResult.Failed(
                CreateDocumentProcessingAttemptFailure.FinalPersistenceError);
        }

        if (attempt.Outcome is not { } outcome
            || attempt.CompletedAtUtc is not { } persistedCompletedAtUtc)
        {
            throw new InvalidOperationException(
                "El intento debe encontrarse finalizado.");
        }

        return CreateDocumentProcessingAttemptResult.Success(
            new CreatedDocumentProcessingAttemptResult(
                attempt.Id,
                source.DocumentId,
                attempt.CorrelationId,
                outcome,
                attempt.ErrorCode,
                null,
                null,
                null,
                null,
                0,
                null,
                null,
                attempt.CreatedAtUtc,
                persistedCompletedAtUtc));
    }

    private static bool HasValidPersistedMetadata(
        DocumentProcessingSource source)
    {
        return !string.IsNullOrWhiteSpace(source.OriginalFileName)
            && string.Equals(
                source.OriginalFileName,
                source.OriginalFileName.Trim(),
                StringComparison.Ordinal)
            && source.OriginalFileName.Length <= 255
            && string.Equals(
                source.ContentType,
                ApplicationPdfContentType,
                StringComparison.Ordinal)
            && source.SizeBytes > 0
            && source.SizeBytes <= MaximumPdfSizeBytes
            && !string.IsNullOrWhiteSpace(source.StorageKey)
            && string.Equals(
                source.StorageKey,
                source.StorageKey.Trim(),
                StringComparison.Ordinal);
    }

    private static string MapClientFailure(
        DocumentProcessingClientResult clientResult)
    {
        if (clientResult.Failure
                == DocumentProcessingClientFailure.RemoteRejection
            && clientResult.RemoteError is { } remoteError
            && !string.IsNullOrWhiteSpace(remoteError.ErrorCode)
            && remoteError.ErrorCode.Length <= 64)
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
}
