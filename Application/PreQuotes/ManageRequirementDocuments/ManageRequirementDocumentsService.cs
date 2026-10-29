using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ManageRequirementDocuments;

public sealed record AddRequirementDocumentCommand(
    Guid RequirementId,
    CreateRequirementFileInput File);

public sealed record RemoveRequirementDocumentCommand(
    Guid RequirementId,
    Guid RequirementFileId);

public sealed record ReplaceRequirementDocumentCommand(
    Guid RequirementId,
    Guid RequirementFileId,
    CreateRequirementFileInput File);

public sealed record ReplaceRequirementCommand(
    Guid RequirementId,
    IReadOnlyList<CreateRequirementFileInput> Files);

public sealed record CancelRequirementCommand(Guid RequirementId);

public enum ManageRequirementDocumentsFailure
{
    None = 0,
    InvalidRequest,
    InvalidFileName,
    UnsupportedFileType,
    EmptyFile,
    FileTooLarge,
    TooManyFiles,
    Unauthorized,
    InactiveUser,
    RequirementNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    DocumentNotFound,
    RequirementNotMutable,
    RequirementNotReplaceable,
    StorageError,
    PersistenceError,
    QueryError
}

public sealed record RequirementLifecycleResult(
    bool IsSuccess,
    ManageRequirementDocumentsFailure Failure,
    RequirementLifecycleReadModel? Requirement)
{
    public static RequirementLifecycleResult Success(
        RequirementLifecycleReadModel requirement) =>
        new(true, ManageRequirementDocumentsFailure.None, requirement);

    public static RequirementLifecycleResult Failed(
        ManageRequirementDocumentsFailure failure) =>
        new(false, failure, null);
}

public sealed record RequirementLifecycleReadModel(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string? CommercialLine,
    string Status,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementDocumentReadModel> Documents);

public sealed class ManageRequirementDocumentsService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<ManageRequirementDocumentsService> logger)
{
    public async Task<RequirementLifecycleResult> AddDocumentAsync(
        AddRequirementDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty || command.File is null)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        var normalized = RequirementFileValidation.NormalizeFile(command.File);
        if (normalized.Failure is { } failure)
        {
            return RequirementLifecycleResult.Failed(MapFileFailure(failure));
        }

        var loaded = await LoadMutableRequirementAsync(
            command.RequirementId,
            cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RequirementLifecycleResult.Failed(loaded.Failure);
        }

        var requirement = loaded.Requirement!;
        if (requirement.Files.Count >= RequirementFileValidation.MaximumFileCount)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.TooManyFiles);
        }

        var totalSize = requirement.Files.Sum(file => file.SizeBytes)
            + normalized.SizeBytes;
        if (totalSize > RequirementFileValidation.MaximumTotalSizeBytes)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.FileTooLarge);
        }

        var now = timeProvider.GetUtcNow();
        var storageKey = RequirementFileValidation.CreateStorageKey(
            requirement.Id,
            normalized.StorageExtension);
        try
        {
            var receipt = await fileStorage.SaveVerifiedAsync(
                storageKey,
                normalized.Content!,
                normalized.SizeBytes,
                RequirementFileValidation.MaximumFileSizeBytes,
                cancellationToken);
            var evidenceAt = timeProvider.GetUtcNow();
            await using var write = await requirementRepository.BeginDocumentWriteAsync(
                command.RequirementId, currentUser.UserId!.Value, cancellationToken);
            if (!write.Capabilities.CanEditDocuments)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotMutable);
            if (write.Files.Count >= RequirementFileValidation.MaximumFileCount)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.TooManyFiles);
            if (write.Files.Sum(f => f.SizeBytes) + normalized.SizeBytes > RequirementFileValidation.MaximumTotalSizeBytes)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.FileTooLarge);
            requirement = write.Requirement;
            now = timeProvider.GetUtcNow();
            requirementRepository.AddFile(RequirementFile.CreateWithStoredEvidence(
                requirement.Id,
                normalized.OriginalFileName,
                normalized.ContentType,
                normalized.SizeBytes,
                storageKey,
                now, receipt.Sha256, receipt.Length, evidenceAt, 1));
            write.PreQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (RequirementDocumentRejectedException exception)
        {
            return RequirementLifecycleResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (exception is ContentVerificationException)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.InvalidRequest);
            LogFailure(exception, requirement.Id, "add_document");
            // A failed write or save does not prove ownership or rollback. Retain the destination.
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }
    }

    public async Task<RequirementLifecycleResult> RemoveDocumentAsync(
        RemoveRequirementDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.RequirementFileId == Guid.Empty)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        var loaded = await LoadMutableRequirementAsync(
            command.RequirementId,
            cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RequirementLifecycleResult.Failed(loaded.Failure);
        }

        var requirement = loaded.Requirement!;
        if (requirement.Files.Count <= 1)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        var file = requirement.Files.SingleOrDefault(value =>
            value.Id == command.RequirementFileId);
        if (file is null)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.DocumentNotFound);
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            await using var write = await requirementRepository.BeginDocumentWriteAsync(
                command.RequirementId, currentUser.UserId!.Value, cancellationToken);
            if (!write.Capabilities.CanEditDocuments)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotMutable);
            file = write.Files.SingleOrDefault(f => f.Id == command.RequirementFileId);
            if (file is null)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.DocumentNotFound);
            if (write.Files.Count <= 1)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.InvalidRequest);
            if (write.Files.Count - 1 > RequirementFileValidation.MaximumFileCount)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.TooManyFiles);
            if (write.Files.Where(f => f.Id != file.Id).Sum(f => f.SizeBytes) > RequirementFileValidation.MaximumTotalSizeBytes)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.FileTooLarge);
            requirement = write.Requirement;
            now = timeProvider.GetUtcNow();
            requirementRepository.RemoveFile(file);
            write.PreQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            // Only the reference is removed; shared or historical bytes require coordinated cleanup.
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (RequirementDocumentRejectedException exception)
        {
            return RequirementLifecycleResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, "remove_document");
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }
    }

    public async Task<RequirementLifecycleResult> ReplaceDocumentAsync(
        ReplaceRequirementDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.RequirementFileId == Guid.Empty
            || command.File is null)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        var normalized = RequirementFileValidation.NormalizeFile(command.File);
        if (normalized.Failure is { } failure)
        {
            return RequirementLifecycleResult.Failed(MapFileFailure(failure));
        }

        var loaded = await LoadMutableRequirementAsync(
            command.RequirementId,
            cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RequirementLifecycleResult.Failed(loaded.Failure);
        }

        var requirement = loaded.Requirement!;
        var oldFile = requirement.Files.SingleOrDefault(value =>
            value.Id == command.RequirementFileId);
        if (oldFile is null)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.DocumentNotFound);
        }

        var totalSize = requirement.Files
            .Where(file => file.Id != command.RequirementFileId)
            .Sum(file => file.SizeBytes)
            + normalized.SizeBytes;
        if (totalSize > RequirementFileValidation.MaximumTotalSizeBytes)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.FileTooLarge);
        }

        var now = timeProvider.GetUtcNow();
        var storageKey = RequirementFileValidation.CreateStorageKey(
            requirement.Id,
            normalized.StorageExtension);
        StoredContentReceipt receipt;
        DateTimeOffset evidenceAt;
        try
        {
            receipt = await fileStorage.SaveVerifiedAsync(
                storageKey,
                normalized.Content!,
                normalized.SizeBytes,
                RequirementFileValidation.MaximumFileSizeBytes,
                cancellationToken);
            evidenceAt = timeProvider.GetUtcNow();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A failed write does not establish ownership of the destination key.
            if (exception is ContentVerificationException)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.InvalidRequest);
            LogFailure(exception, requirement.Id, "replace_document_storage");
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }

        try
        {
            await using var write = await requirementRepository.BeginDocumentWriteAsync(
                command.RequirementId, currentUser.UserId!.Value, cancellationToken);
            if (!write.Capabilities.CanEditDocuments)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotMutable);
            oldFile = write.Files.SingleOrDefault(f => f.Id == command.RequirementFileId);
            if (oldFile is null)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.DocumentNotFound);
            if (write.Files.Count > RequirementFileValidation.MaximumFileCount)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.TooManyFiles);
            if (write.Files.Where(f => f.Id != oldFile.Id).Sum(f => f.SizeBytes) + normalized.SizeBytes
                > RequirementFileValidation.MaximumTotalSizeBytes)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.FileTooLarge);
            requirement = write.Requirement;
            now = timeProvider.GetUtcNow();
            requirementRepository.AddFile(RequirementFile.CreateWithStoredEvidence(
                requirement.Id,
                normalized.OriginalFileName,
                normalized.ContentType,
                normalized.SizeBytes,
                storageKey,
                now, receipt.Sha256, receipt.Length, evidenceAt, 1));
            requirementRepository.RemoveFile(oldFile);
            write.PreQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
        }
        catch (RequirementDocumentRejectedException exception)
        {
            return RequirementLifecycleResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // SaveChanges may have committed. Retain both files on an uncertain outcome.
            LogFailure(exception, requirement.Id, "replace_document_persistence");
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }

        // References are saved; old bytes await coordinated cleanup, not inline deletion.
        try
        {
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, "replace_document_response");
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }
    }

    public async Task<RequirementLifecycleResult> CancelAsync(
        CancelRequirementCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        var loaded = await LoadRequirementAsync(
            command.RequirementId,
            cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RequirementLifecycleResult.Failed(loaded.Failure);
        }

        var requirement = loaded.Requirement!;
        if (!(await requirementRepository.GetDocumentCapabilitiesAsync(
                requirement.Id, currentUser.UserId!.Value, cancellationToken)).CanCancel)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.RequirementNotMutable);
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            await using var write = await requirementRepository.BeginDocumentWriteAsync(
                command.RequirementId, currentUser.UserId!.Value, cancellationToken);
            if (!write.Capabilities.CanCancel)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotMutable);
            requirement = write.Requirement;
            now = timeProvider.GetUtcNow();
            requirement.Cancel(now);
            write.PreQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (RequirementDocumentRejectedException exception)
        {
            return RequirementLifecycleResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, "cancel");
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }
    }

    public async Task<RequirementLifecycleResult> ReplaceRequirementAsync(
        ReplaceRequirementCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.Files is null
            || command.Files.Count == 0)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.InvalidRequest);
        }

        if (command.Files.Count > RequirementFileValidation.MaximumFileCount)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.TooManyFiles);
        }

        var normalizedFiles = new List<NormalizedRequirementFile>(
            command.Files.Count);
        long totalSize = 0;
        foreach (var file in command.Files)
        {
            var normalized = RequirementFileValidation.NormalizeFile(file);
            if (normalized.Failure is { } failure)
            {
                return RequirementLifecycleResult.Failed(MapFileFailure(failure));
            }

            totalSize += normalized.SizeBytes;
            if (totalSize > RequirementFileValidation.MaximumTotalSizeBytes)
            {
                return RequirementLifecycleResult.Failed(
                    ManageRequirementDocumentsFailure.FileTooLarge);
            }

            normalizedFiles.Add(normalized);
        }

        var loaded = await LoadRequirementAsync(
            command.RequirementId,
            cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RequirementLifecycleResult.Failed(loaded.Failure);
        }

        var oldRequirement = loaded.Requirement!;
        if (!(await requirementRepository.GetDocumentCapabilitiesAsync(
                oldRequirement.Id, currentUser.UserId!.Value, cancellationToken)).CanReplace)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.RequirementNotReplaceable);
        }

        var now = timeProvider.GetUtcNow();
        var replacement = Requirement.Create(
            oldRequirement.PreQuoteId,
            oldRequirement.CreatedByUserId,
            oldRequirement.CommercialLine
                ?? RequirementCommercialLine.Essential,
            now);
        replacement.MarkAsReplacementOf(oldRequirement.Id);

        try
        {
            var preparedFiles = new List<RequirementFile>();
            foreach (var file in normalizedFiles)
            {
                var storageKey = RequirementFileValidation.CreateStorageKey(
                    replacement.Id,
                    file.StorageExtension);
                var receipt = await fileStorage.SaveVerifiedAsync(
                    storageKey,
                    file.Content!,
                    file.SizeBytes,
                    RequirementFileValidation.MaximumFileSizeBytes,
                    cancellationToken);
                var evidenceAt = timeProvider.GetUtcNow();
                preparedFiles.Add(RequirementFile.CreateWithStoredEvidence(
                    replacement.Id,
                    file.OriginalFileName,
                    file.ContentType,
                    file.SizeBytes,
                    storageKey,
                    now, receipt.Sha256, receipt.Length, evidenceAt, 1));
            }

            await using var write = await requirementRepository.BeginDocumentWriteAsync(
                command.RequirementId, currentUser.UserId!.Value, cancellationToken);
            if (!write.Capabilities.CanReplace)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotReplaceable);
            if (write.Requirement.PreQuoteId != oldRequirement.PreQuoteId
                || write.Requirement.CreatedByUserId != oldRequirement.CreatedByUserId
                || write.Requirement.CommercialLine != oldRequirement.CommercialLine)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.RequirementNotReplaceable);
            oldRequirement = write.Requirement;
            now = timeProvider.GetUtcNow();
            requirementRepository.Add(replacement);
            foreach (var file in preparedFiles) requirementRepository.AddFile(file);
            if (oldRequirement.Status == RequirementStatus.Pending)
                oldRequirement.SupersedeAfterConfirmedCancellationBy(replacement.Id, now);
            else
                oldRequirement.SupersedeBy(replacement.Id, now);
            write.PreQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(replacement, now, cancellationToken));
        }
        catch (RequirementDocumentRejectedException exception)
        {
            return RequirementLifecycleResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (exception is ContentVerificationException)
                return RequirementLifecycleResult.Failed(ManageRequirementDocumentsFailure.InvalidRequest);
            LogFailure(exception, oldRequirement.Id, "replace_requirement");
            // A save or response failure may follow commit. Keep the complete batch and old files.

            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.PersistenceError);
        }
    }

    private async Task<LoadedRequirement> LoadMutableRequirementAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadRequirementAsync(requirementId, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        if (!(await requirementRepository.GetDocumentCapabilitiesAsync(
                requirementId, currentUser.UserId!.Value, cancellationToken)).CanEditDocuments)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.RequirementNotMutable);
        }

        return loaded;
    }

    private async Task<LoadedRequirement> LoadRequirementAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.InactiveUser);
        }

        var requirement = await requirementRepository.FindByIdForUpdateAsync(
            requirementId,
            cancellationToken);
        if (requirement is null)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.RequirementNotFound);
        }

        var preQuote = await preQuoteRepository.FindForUpdateByIdAsync(
            requirement.PreQuoteId,
            cancellationToken);
        if (preQuote is null)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.PreQuoteNotFound);
        }

        var project = await projectRepository.FindByIdAsync(
            preQuote.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.ProjectNotFound);
        }

        if (project.CreatedByUserId != userId)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.RequirementNotFound);
        }

        if (!project.IsActive)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.InactiveProject);
        }

        var client = await clientRepository.FindByIdAsync(
            project.ClientId,
            cancellationToken);
        if (client is null)
        {
            return LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.ClientNotFound);
        }

        return client.IsActive
            ? LoadedRequirement.Success(requirement, preQuote)
            : LoadedRequirement.Failed(
                ManageRequirementDocumentsFailure.InactiveClient);
    }

    private async Task<RequirementLifecycleReadModel> MapAsync(
        Requirement requirement,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var documents = await requirementRepository
            .ListDocumentReadModelsByRequirementIdAsync(
                requirement.Id,
                cancellationToken);

        var capabilities = await requirementRepository.GetDocumentCapabilitiesAsync(
            requirement.Id, currentUser.UserId!.Value, cancellationToken);
        return Map(requirement, updatedAtUtc, documents, capabilities);
    }

    private static RequirementLifecycleReadModel Map(
        Requirement requirement,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<RequirementDocumentReadModel> documents,
        RequirementDocumentCapabilities capabilities) =>
        new(
            requirement.Id,
            requirement.PreQuoteId,
            documents.Count,
            requirement.CommercialLine?.ToString().ToUpperInvariant(),
            requirement.Status.ToString().ToUpperInvariant(),
            capabilities.CanEditDocuments,
            capabilities.CanCancel,
            capabilities.CanReplace,
            requirement.IsCurrent,
            requirement.SupersedesRequirementId,
            requirement.SupersededByRequirementId,
            updatedAtUtc,
            documents);

    private static ManageRequirementDocumentsFailure MapFileFailure(
        CreateRequirementFailure failure) =>
        failure switch
        {
            CreateRequirementFailure.InvalidFileName =>
                ManageRequirementDocumentsFailure.InvalidFileName,
            CreateRequirementFailure.UnsupportedFileType =>
                ManageRequirementDocumentsFailure.UnsupportedFileType,
            CreateRequirementFailure.EmptyFile =>
                ManageRequirementDocumentsFailure.EmptyFile,
            CreateRequirementFailure.FileTooLarge =>
                ManageRequirementDocumentsFailure.FileTooLarge,
            CreateRequirementFailure.TooManyFiles =>
                ManageRequirementDocumentsFailure.TooManyFiles,
            _ => ManageRequirementDocumentsFailure.InvalidRequest
        };

    private void LogFailure(Exception exception, Guid requirementId, string stage)
    {
        logger.LogError(
            exception,
            "Requirement document lifecycle failed. RequirementId={RequirementId} Stage={Stage} ExceptionType={ExceptionType}",
            requirementId,
            stage,
            exception.GetType().Name);
    }

    private sealed record LoadedRequirement(
        bool IsSuccess,
        ManageRequirementDocumentsFailure Failure,
        Requirement? Requirement,
        PreQuote? PreQuote)
    {
        public static LoadedRequirement Success(
            Requirement requirement,
            PreQuote preQuote) =>
            new(true, ManageRequirementDocumentsFailure.None, requirement, preQuote);

        public static LoadedRequirement Failed(
            ManageRequirementDocumentsFailure failure) =>
            new(false, failure, null, null);
    }
}
