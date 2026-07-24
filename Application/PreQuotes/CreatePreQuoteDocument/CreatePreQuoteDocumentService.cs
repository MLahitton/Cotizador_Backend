using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.CreatePreQuoteDocument;

public sealed class CreatePreQuoteDocumentService(
    IValidator<CreatePreQuoteDocumentCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IFileStorage fileStorage)
{
    private const long MaximumFileSizeBytes = 20 * 1024 * 1024;

    public async Task<CreatePreQuoteDocumentResult> ExecuteAsync(
        CreatePreQuoteDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InvalidRequest);
        }

        var originalFileName = command.OriginalFileName?.Trim();

        if (string.IsNullOrWhiteSpace(originalFileName)
            || originalFileName.Length > 255)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InvalidFileName);
        }

        var contentType = command.ContentType?.Trim().ToLowerInvariant();

        if (!string.Equals(
                contentType,
                "application/pdf",
                StringComparison.Ordinal))
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.UnsupportedFileType);
        }

        if (command.SizeBytes < 0)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InvalidRequest);
        }

        if (command.SizeBytes == 0)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.EmptyFile);
        }

        if (command.SizeBytes > MaximumFileSizeBytes)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.FileTooLarge);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InactiveUser);
        }

        PreQuote? preQuote;

        try
        {
            preQuote = await preQuoteRepository.FindForUpdateByIdAsync(
                command.PreQuoteId,
                cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.QueryError);
        }

        if (preQuote is null)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.PreQuoteNotFound);
        }

        Domain.Projects.Project? project;

        try
        {
            project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.QueryError);
        }

        if (project is null)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.ProjectNotFound);
        }

        if (!project.IsActive)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InactiveProject);
        }

        Domain.Clients.Client? client;

        try
        {
            client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.QueryError);
        }

        if (client is null)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.InactiveClient);
        }

        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var storageKey =
            $"prequotes/{preQuote.Id:D}/documents/{documentId:D}/original.pdf";
        var document = PreQuoteDocument.Create(
            documentId,
            preQuote.Id,
            originalFileName,
            contentType!,
            command.SizeBytes,
            storageKey,
            userId,
            now);

        try
        {
            await fileStorage.SaveAsync(
                storageKey,
                command.Content!,
                cancellationToken);
        }
        catch (InvalidStorageKeyException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.StorageError);
        }
        catch (FileStorageConflictException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.StorageError);
        }
        catch (FileStorageWriteException)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.StorageError);
        }

        try
        {
            preQuoteRepository.AddDocument(document);
            preQuote.RegisterActivity(now);

            await preQuoteRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PreQuotePersistenceException)
        {
            var failure = await CompensateAsync(storageKey);
            return CreatePreQuoteDocumentResult.Failed(failure);
        }
        catch (ArgumentException)
        {
            var failure = await CompensateAsync(storageKey);
            return CreatePreQuoteDocumentResult.Failed(failure);
        }

        return CreatePreQuoteDocumentResult.Success(
            new CreatedPreQuoteDocumentResult(
                document.Id,
                document.PreQuoteId,
                document.OriginalFileName,
                document.ContentType,
                document.SizeBytes,
                document.CreatedAtUtc));
    }

    private async Task<CreatePreQuoteDocumentFailure> CompensateAsync(
        string storageKey)
    {
        try
        {
            await fileStorage.DeleteIfExistsAsync(
                storageKey,
                CancellationToken.None);

            return CreatePreQuoteDocumentFailure.PersistenceError;
        }
        catch (InvalidStorageKeyException)
        {
            return CreatePreQuoteDocumentFailure.CompensationError;
        }
        catch (FileStorageDeleteException)
        {
            return CreatePreQuoteDocumentFailure.CompensationError;
        }
    }
}
