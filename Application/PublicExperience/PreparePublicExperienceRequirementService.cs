using System.Security.Cryptography;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging;

namespace Application.PublicExperience;

public sealed class PreparePublicExperienceRequirementService(
    IPublicExperienceRequirementPreparationRepository submissions,
    IProjectRepository projects,
    IPreQuoteRepository preQuotes,
    IRequirementRepository requirements,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<PreparePublicExperienceRequirementService> logger)
{
    // PUBLIC_EXPERIENCE_REQUIREMENT_COMMERCIAL_LINE_BRIDGE = ESSENTIAL.
    // Legacy technical bridge only: public answers do not determine commercial line.
    public const RequirementCommercialLine CommercialLineBridge = RequirementCommercialLine.Essential;

    public Task<PublicExperienceRequirementPreparationResult?> ExecuteAsync(
        string submissionId, CancellationToken cancellationToken)
    {
        var copiedKeys = new List<string>();
        return submissions.PrepareLockedAsync(submissionId,
            (submission, token) => PrepareAsync(submission, copiedKeys, token),
            () => CompensateAsync(copiedKeys), cancellationToken);
    }

    private async Task PrepareAsync(PublicExperienceSubmission submission, List<string> copiedKeys,
        CancellationToken cancellationToken)
    {
        if (submission.ClientId is null || submission.ProjectId is null)
            throw Failure(PublicExperienceRequirementPreparationErrors.NotReady);
        var project = await projects.FindByIdAsync(submission.ProjectId.Value, cancellationToken);
        if (project is null || !project.IsActive || project.ClientId != submission.ClientId
            || project.CreatedByUserId == Guid.Empty)
            throw Failure(PublicExperienceRequirementPreparationErrors.InvalidProject);
        var files = submission.Files.OrderBy(file => file.Sequence).ToArray();
        if (files.Length == 0)
            throw Failure(PublicExperienceRequirementPreparationErrors.DocumentsRequired);

        // Reuse the Requirement metadata helper; intake's stricter policy remains authoritative.
        var normalizedFiles = files.Select(file => RequirementFileValidation.NormalizeFile(
            new CreateRequirementFileInput(file.OriginalFileName, file.ContentType, file.SizeBytes, Stream.Null))).ToArray();
        if (normalizedFiles.Any(file => file.Failure is not null))
            throw Failure(PublicExperienceRequirementPreparationErrors.FileMetadata);

        var now = timeProvider.GetUtcNow();
        var serial = await preQuotes.ReserveNextSerialAsync(now, cancellationToken);
        var preQuote = PreQuote.Create(project.Id, project.CreatedByUserId, serial, null, now);
        var requirement = Requirement.Create(preQuote.Id, project.CreatedByUserId, CommercialLineBridge, now);
        preQuotes.Add(preQuote);
        requirements.Add(requirement);
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            await using var original = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
            using var copy = new MemoryStream();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await original.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (copy.Length + read > file.SizeBytes)
                    throw Failure(PublicExperienceRequirementPreparationErrors.FileIntegrity);
                hash.AppendData(buffer, 0, read);
                await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (copy.Length != file.SizeBytes
                || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw Failure(PublicExperienceRequirementPreparationErrors.FileIntegrity);
            copy.Position = 0;
            var key = RequirementFileValidation.CreateStorageKey(requirement.Id, normalizedFiles[index].StorageExtension);
            // Include a partially written key in compensation if SaveAsync throws.
            copiedKeys.Add(key);
            await storage.SaveAsync(key, copy, cancellationToken);
            // Existing readers order by CreatedAtUtc then Id. PostgreSQL stores microseconds.
            requirements.AddFile(RequirementFile.Create(requirement.Id, file.OriginalFileName,
                file.ContentType, file.SizeBytes, key, now.AddTicks(index * 10L)));
        }
        submission.CompleteRequirementPreparation(preQuote.Id, requirement.Id, now);
    }

    private async Task CompensateAsync(IReadOnlyList<string> copiedKeys)
    {
        foreach (var key in copiedKeys)
        {
            try { await storage.DeleteIfExistsAsync(key, CancellationToken.None); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Public experience requirement copy cleanup failed. StorageKey={StorageKey}", key);
            }
        }
    }

    private static PublicExperienceRequirementPreparationException Failure(string code) => new(code);
}
