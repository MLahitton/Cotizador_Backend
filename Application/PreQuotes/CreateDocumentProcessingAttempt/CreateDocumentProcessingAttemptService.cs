using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.CreateDocumentProcessingAttempt;

public sealed class CreateDocumentProcessingAttemptService(
    IValidator<CreateDocumentProcessingAttemptCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IDocumentProcessingRepository documentProcessingRepository,
    TimeProvider timeProvider)
{
    private const long MaximumPdfSizeBytes = 20 * 1024 * 1024;
    private const string ApplicationPdfContentType = "application/pdf";

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
}
