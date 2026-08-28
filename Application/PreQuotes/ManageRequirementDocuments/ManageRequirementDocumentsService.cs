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
            await fileStorage.SaveAsync(
                storageKey,
                normalized.Content!,
                cancellationToken);
            requirementRepository.AddFile(RequirementFile.Create(
                requirement.Id,
                normalized.OriginalFileName,
                normalized.ContentType,
                normalized.SizeBytes,
                storageKey,
                now));
            loaded.PreQuote!.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, "add_document");
            await fileStorage.DeleteIfExistsAsync(storageKey, CancellationToken.None);
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
            requirementRepository.RemoveFile(file);
            loaded.PreQuote!.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await fileStorage.DeleteIfExistsAsync(file.StorageKey, CancellationToken.None);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
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
        try
        {
            await fileStorage.SaveAsync(
                storageKey,
                normalized.Content!,
                cancellationToken);
            requirementRepository.AddFile(RequirementFile.Create(
                requirement.Id,
                normalized.OriginalFileName,
                normalized.ContentType,
                normalized.SizeBytes,
                storageKey,
                now));
            requirementRepository.RemoveFile(oldFile);
            loaded.PreQuote!.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            await fileStorage.DeleteIfExistsAsync(
                oldFile.StorageKey,
                CancellationToken.None);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, "replace_document");
            await fileStorage.DeleteIfExistsAsync(storageKey, CancellationToken.None);
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
        if (!requirement.CanCancel)
        {
            return RequirementLifecycleResult.Failed(
                ManageRequirementDocumentsFailure.RequirementNotMutable);
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            requirement.Cancel(now);
            loaded.PreQuote!.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(requirement, now, cancellationToken));
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
        if (!oldRequirement.CanReplace)
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
        var storedKeys = new List<string>(normalizedFiles.Count);

        try
        {
            requirementRepository.Add(replacement);
            foreach (var file in normalizedFiles)
            {
                var storageKey = RequirementFileValidation.CreateStorageKey(
                    replacement.Id,
                    file.StorageExtension);
                await fileStorage.SaveAsync(
                    storageKey,
                    file.Content!,
                    cancellationToken);
                storedKeys.Add(storageKey);
                requirementRepository.AddFile(RequirementFile.Create(
                    replacement.Id,
                    file.OriginalFileName,
                    file.ContentType,
                    file.SizeBytes,
                    storageKey,
                    now));
            }

            oldRequirement.SupersedeBy(replacement.Id, now);
            loaded.PreQuote!.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            return RequirementLifecycleResult.Success(
                await MapAsync(replacement, now, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, oldRequirement.Id, "replace_requirement");
            foreach (var storageKey in storedKeys)
            {
                await fileStorage.DeleteIfExistsAsync(storageKey, CancellationToken.None);
            }

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

        if (!loaded.Requirement!.CanEditDocuments)
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

        return Map(requirement, updatedAtUtc, documents);
    }

    private static RequirementLifecycleReadModel Map(
        Requirement requirement,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<RequirementDocumentReadModel> documents) =>
        new(
            requirement.Id,
            requirement.PreQuoteId,
            documents.Count,
            requirement.CommercialLine?.ToString().ToUpperInvariant(),
            requirement.Status.ToString().ToUpperInvariant(),
            requirement.CanEditDocuments,
            requirement.CanCancel,
            requirement.CanReplace,
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
