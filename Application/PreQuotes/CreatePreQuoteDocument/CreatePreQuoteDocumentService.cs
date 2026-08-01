using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.CreatePreQuoteDocument;

public sealed class CreatePreQuoteDocumentService(
    IValidator<CreatePreQuoteDocumentCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IFileStorage fileStorage,
    ILogger<CreatePreQuoteDocumentService> logger)
{
    public const long MaximumFileSizeBytes = 20 * 1024 * 1024;

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

        Domain.Identity.User? user;
        try
        {
            user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "identity_query");
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.QueryError);
        }

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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "prequote_query");
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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "project_query");
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.QueryError);
        }

        if (project is null)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.ProjectNotFound);
        }

        if (project.CreatedByUserId != userId)
        {
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.PreQuoteNotFound);
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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "client_query");
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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "storage");
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.StorageError);
        }

        try
        {
            preQuoteRepository.AddDocument(document);
            preQuote.RegisterActivity(now);

            await preQuoteRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command, userId, "persistence");
            await CompensateAsync(storageKey, command, userId);
            return CreatePreQuoteDocumentResult.Failed(
                CreatePreQuoteDocumentFailure.PersistenceError);
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

    private async Task CompensateAsync(
        string storageKey,
        CreatePreQuoteDocumentCommand command,
        Guid userId)
    {
        try
        {
            await fileStorage.DeleteIfExistsAsync(
                storageKey,
                CancellationToken.None);

        }
        catch (Exception exception)
        {
            LogFailure(exception, command, userId, "compensation");
        }
    }

    private void LogFailure(
        Exception exception,
        CreatePreQuoteDocumentCommand command,
        Guid userId,
        string stage)
    {
        logger.LogError(
            "Document upload failed. PreQuoteId={PreQuoteId} UserId={UserId} Stage={Stage} TraceId={TraceId} SizeBytes={SizeBytes} ContentType={ContentType} ExceptionType={ExceptionType}",
            command.PreQuoteId,
            userId,
            stage,
            System.Diagnostics.Activity.Current?.Id,
            command.SizeBytes,
            command.ContentType,
            exception.GetType().Name);
    }
}
