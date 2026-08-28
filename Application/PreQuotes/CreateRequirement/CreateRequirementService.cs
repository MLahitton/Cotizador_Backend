using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.CreateRequirement;

public sealed class CreateRequirementService(
    IValidator<CreateRequirementCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IRequirementRepository requirementRepository,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<CreateRequirementService> logger)
{
    public const int MaximumFileCount = RequirementFileValidation.MaximumFileCount;
    public const long MaximumFileSizeBytes =
        RequirementFileValidation.MaximumFileSizeBytes;
    public const long MaximumTotalSizeBytes =
        RequirementFileValidation.MaximumTotalSizeBytes;

    public async Task<CreateRequirementResult> ExecuteAsync(
        CreateRequirementCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InvalidRequest);
        }

        if (!TryParseCommercialLine(
                command.CommercialLine,
                out var commercialLine))
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InvalidRequest);
        }

        if (command.Files.Count == 0)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InvalidRequest);
        }

        if (command.Files.Count > MaximumFileCount)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.TooManyFiles);
        }

        var normalizedFiles =
            new List<NormalizedRequirementFile>(command.Files.Count);
        long totalSizeBytes = 0;

        foreach (var file in command.Files)
        {
            var normalizedFile = RequirementFileValidation.NormalizeFile(file);
            if (normalizedFile.Failure is { } failure)
            {
                return CreateRequirementResult.Failed(failure);
            }

            totalSizeBytes += normalizedFile.SizeBytes;
            if (totalSizeBytes > MaximumTotalSizeBytes)
            {
                return CreateRequirementResult.Failed(
                    CreateRequirementFailure.FileTooLarge);
            }

            normalizedFiles.Add(normalizedFile);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.Unauthorized);
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
            LogFailure(exception, command.PreQuoteId, userId, "identity_query");
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.QueryError);
        }

        if (user is null)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InactiveUser);
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
            LogFailure(exception, command.PreQuoteId, userId, "prequote_query");
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.QueryError);
        }

        if (preQuote is null)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.PreQuoteNotFound);
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
            LogFailure(exception, command.PreQuoteId, userId, "project_query");
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.QueryError);
        }

        if (project is null)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.ProjectNotFound);
        }

        if (project.CreatedByUserId != userId)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.PreQuoteNotFound);
        }

        if (!project.IsActive)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InactiveProject);
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
            LogFailure(exception, command.PreQuoteId, userId, "client_query");
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.QueryError);
        }

        if (client is null)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return CreateRequirementResult.Failed(
                CreateRequirementFailure.InactiveClient);
        }

        var now = timeProvider.GetUtcNow();
        var requirement = Requirement.Create(
            preQuote.Id,
            userId,
            commercialLine,
            now);
        var storedKeys = new List<string>(normalizedFiles.Count);
        var persistedFiles = new List<RequirementFile>(normalizedFiles.Count);

        try
        {
            requirementRepository.Add(requirement);

            foreach (var file in normalizedFiles)
            {
                var storageKey = RequirementFileValidation.CreateStorageKey(
                    requirement.Id,
                    file.StorageExtension);
                await fileStorage.SaveAsync(
                    storageKey,
                    file.Content!,
                    cancellationToken);
                storedKeys.Add(storageKey);

                var requirementFile = RequirementFile.Create(
                    requirement.Id,
                    file.OriginalFileName,
                    file.ContentType,
                    file.SizeBytes,
                    storageKey,
                    now);
                persistedFiles.Add(requirementFile);
                requirementRepository.AddFile(requirementFile);
            }

            preQuote.RegisterActivity(now);
            await requirementRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.PreQuoteId, userId, "storage_or_persistence");
            await CompensateAsync(storedKeys);
            return CreateRequirementResult.Failed(
                storedKeys.Count < normalizedFiles.Count
                    ? CreateRequirementFailure.StorageError
                    : CreateRequirementFailure.PersistenceError);
        }

        return CreateRequirementResult.Success(
            new CreatedRequirementResult(
                requirement.Id,
                requirement.PreQuoteId,
                normalizedFiles.Count,
                ToContract(commercialLine),
                "PENDING",
                requirement.CreatedAtUtc,
                persistedFiles
                    .OrderBy(file => file.CreatedAtUtc)
                    .ThenBy(file => file.Id)
                    .Select(file => new CreatedRequirementDocumentResult(
                        file.Id,
                        file.OriginalFileName,
                        file.ContentType,
                        file.SizeBytes,
                        file.CreatedAtUtc))
                    .ToArray()));
    }

    private static bool TryParseCommercialLine(
        string? value,
        out RequirementCommercialLine commercialLine)
    {
        commercialLine = default;
        return value?.Trim() switch
        {
            "CLASSIC" => Set(out commercialLine, RequirementCommercialLine.Classic),
            "ESSENTIAL" => Set(out commercialLine, RequirementCommercialLine.Essential),
            "BIOCONFORT" => Set(out commercialLine, RequirementCommercialLine.Bioconfort),
            "SIGNATURE" => Set(out commercialLine, RequirementCommercialLine.Signature),
            _ => false
        };
    }

    private static bool Set(
        out RequirementCommercialLine target,
        RequirementCommercialLine value)
    {
        target = value;
        return true;
    }

    private static string ToContract(RequirementCommercialLine commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => "CLASSIC",
            RequirementCommercialLine.Essential => "ESSENTIAL",
            RequirementCommercialLine.Bioconfort => "BIOCONFORT",
            RequirementCommercialLine.Signature => "SIGNATURE",
            _ => throw new ArgumentOutOfRangeException(nameof(commercialLine))
        };

    private async Task CompensateAsync(IReadOnlyList<string> storageKeys)
    {
        foreach (var storageKey in storageKeys)
        {
            try
            {
                await fileStorage.DeleteIfExistsAsync(
                    storageKey,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Requirement upload compensation failed. StorageKey={StorageKey} ExceptionType={ExceptionType}",
                    storageKey,
                    exception.GetType().Name);
            }
        }
    }

    private void LogFailure(
        Exception exception,
        Guid preQuoteId,
        Guid userId,
        string stage)
    {
        logger.LogError(
            exception,
            "Requirement upload failed. PreQuoteId={PreQuoteId} UserId={UserId} Stage={Stage} TraceId={TraceId} ExceptionType={ExceptionType}",
            preQuoteId,
            userId,
            stage,
            System.Diagnostics.Activity.Current?.Id,
            exception.GetType().Name);
    }

}
