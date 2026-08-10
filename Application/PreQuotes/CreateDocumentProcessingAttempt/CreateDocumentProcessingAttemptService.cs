using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using FluentValidation;
using System.IO;

namespace Application.PreQuotes.CreateDocumentProcessingAttempt;

public sealed class CreateDocumentProcessingAttemptService(
    IValidator<CreateDocumentProcessingAttemptCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IDocumentProcessingRepository documentProcessingRepository,
    TimeProvider timeProvider)
{
    private const long MaximumSupportedFileSizeBytes = 20 * 1024 * 1024;
    private const string ApplicationPdfContentType = "application/pdf";
    private const string ApplicationXlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<CreateDocumentProcessingAttemptResult> ExecuteAsync(
        CreateDocumentProcessingAttemptCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.InactiveUser);
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
            return Failed(CreateDocumentProcessingAttemptFailure.QueryError);
        }

        if (source is null)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.DocumentNotFound);
        }

        if (source.ProjectCreatedByUserId != userId)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.DocumentNotFound);
        }

        if (!source.ProjectIsActive)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.InactiveProject);
        }

        if (!source.ClientIsActive)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.InactiveClient);
        }

        if (!HasValidPersistedMetadata(source))
        {
            return Failed(CreateDocumentProcessingAttemptFailure.QueryError);
        }

        try
        {
            if (await documentProcessingRepository
                    .HasActiveDocumentProcessingAttemptAsync(
                        source.DocumentId,
                        cancellationToken))
            {
                return Failed(
                    CreateDocumentProcessingAttemptFailure
                        .DocumentProcessingAlreadyActive);
            }
        }
        catch (DocumentProcessingQueryException)
        {
            return Failed(CreateDocumentProcessingAttemptFailure.QueryError);
        }

        var attempt = DocumentProcessingAttempt.Create(
            source.DocumentId,
            userId,
            Guid.NewGuid(),
            timeProvider.GetUtcNow());

        try
        {
            documentProcessingRepository.AddAttempt(attempt);
            await documentProcessingRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (DocumentProcessingActiveAttemptConflictException)
        {
            return Failed(
                CreateDocumentProcessingAttemptFailure
                    .DocumentProcessingAlreadyActive);
        }
        catch (DocumentProcessingPersistenceException)
        {
            return Failed(
                CreateDocumentProcessingAttemptFailure.InitialPersistenceError);
        }

        return CreateDocumentProcessingAttemptResult.Success(
            new DocumentProcessingAttemptStatusData(
                attempt.Id,
                attempt.PreQuoteDocumentId,
                attempt.ProcessingState,
                attempt.Outcome,
                attempt.ErrorCode,
                attempt.CreatedAtUtc,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                null));
    }

    private static CreateDocumentProcessingAttemptResult Failed(
        CreateDocumentProcessingAttemptFailure failure)
    {
        return CreateDocumentProcessingAttemptResult.Failed(failure);
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
            && IsSupportedContentType(source.ContentType)
            && source.SizeBytes > 0
            && !string.IsNullOrWhiteSpace(source.StorageKey)
            && string.Equals(
                source.StorageKey,
                source.StorageKey.Trim(),
                StringComparison.Ordinal)
            && source.SizeBytes <= MaximumSupportedFileSizeBytes
            && IsSupportedExtensionForContentType(
                source.OriginalFileName,
                source.ContentType,
                source.StorageKey);
    }

    private static bool IsSupportedExtensionForContentType(
        string fileName,
        string contentType,
        string storageKey)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.Equals(
                extension,
                contentType switch
                {
                    var t when string.Equals(
                        t,
                        ApplicationPdfContentType,
                        StringComparison.OrdinalIgnoreCase) => ".pdf",
                    var t when string.Equals(
                        t,
                        ApplicationXlsxContentType,
                        StringComparison.OrdinalIgnoreCase) => ".xlsx",
                    _ => null
                },
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(storageKey))
        {
        return extension is not null
                && string.Equals(
                    Path.GetExtension(storageKey),
                    extension,
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsSupportedContentType(string contentType)
    {
        return string.Equals(
            contentType,
            ApplicationPdfContentType,
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                contentType,
                ApplicationXlsxContentType,
                StringComparison.OrdinalIgnoreCase);
    }
}
