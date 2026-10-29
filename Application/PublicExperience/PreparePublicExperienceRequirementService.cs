using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PublicExperience;

public sealed class PreparePublicExperienceRequirementService(
    IPublicExperienceRequirementPreparationRepository submissions,
    IFileStorage storage, IVerifiedContentCopy copies, TimeProvider timeProvider,
    ILogger<PreparePublicExperienceRequirementService> logger)
{
    // PUBLIC_EXPERIENCE_REQUIREMENT_COMMERCIAL_LINE_BRIDGE = ESSENTIAL.
    public const RequirementCommercialLine CommercialLineBridge = RequirementCommercialLine.Essential;

    public async Task<PublicExperienceRequirementPreparationResult?> ExecuteAsync(
        string submissionId, CancellationToken cancellationToken)
    {
        var read = await submissions.ReadAsync(submissionId, cancellationToken);
        if (read.Result is not null || read.Snapshot is null) return read.Result;
        var snapshot = read.Snapshot;
        var prepared = new List<PublicPreparedFile>();
        Requirement candidate;
        try
        {
            if (snapshot.ErrorCode is not null) throw Failure(snapshot.ErrorCode);
            if (snapshot.Files.Count == 0) throw Failure(PublicExperienceRequirementPreparationErrors.DocumentsRequired);
            var normalized = snapshot.Files.Select(f => RequirementFileValidation.NormalizeFile(
                new CreateRequirementFileInput(f.OriginalFileName, f.ContentType, f.SizeBytes, Stream.Null))).ToArray();
            if (normalized.Any(f => f.Failure is not null))
                throw Failure(PublicExperienceRequirementPreparationErrors.FileMetadata);
            // PostgreSQL preserves microseconds; normalize before assigning document order.
            var now = timeProvider.GetUtcNow();
            now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
            candidate = Requirement.Create(Guid.NewGuid(), snapshot.OwnerId!.Value, CommercialLineBridge, now);
            for (var i = 0; i < snapshot.Files.Count; i++)
            {
                var file = snapshot.Files[i];
                var hash = ExpectedHash(file);
                await using var source = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
                await using var copy = await copies.PrepareAsync(source, file.SizeBytes, hash,
                    PublicExperiencePayloadValidation.MaximumFileBytes, cancellationToken);
                await using var content = copy.OpenRead();
                var key = RequirementFileValidation.CreateStorageKey(candidate.Id, normalized[i].StorageExtension);
                var receipt = await storage.SaveVerifiedAsync(key, content, file.SizeBytes,
                    PublicExperiencePayloadValidation.MaximumFileBytes, cancellationToken);
                var at = timeProvider.GetUtcNow();
                if (receipt.Sha256 != hash || receipt.Length != file.SizeBytes
                    || receipt.Sha256 != copy.Sha256 || receipt.Length != copy.Length)
                    throw new FileStorageWriteException(new ContentVerificationException());
                prepared.Add(new(file.Id, key, receipt, at));
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var code = exception switch
            {
                PublicExperienceRequirementPreparationException expected => expected.ErrorCode,
                ContentVerificationException => PublicExperienceRequirementPreparationErrors.FileIntegrity,
                _ => PublicExperienceRequirementPreparationErrors.Failed
            };
            logger.LogWarning("Public preparation copy failed; destinations retained. CompletedCount={Count}", prepared.Count);
            return await submissions.FailAsync(snapshot, code, cancellationToken);
        }
        // An uncertain commit must not fall through to a compensating failure write.
        return await submissions.CommitAsync(snapshot, candidate, prepared, cancellationToken);
    }

    private static string ExpectedHash(PublicPreparationFile file)
    {
        var hash = file.ContentHash;
        if (hash.Length != 64 || hash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f')))
            throw Failure(PublicExperienceRequirementPreparationErrors.FileIntegrity);
        var unknown = file.StoredLengthBytes is null && file.StoredEvidenceAtUtc is null && file.StoredEvidenceVersion is null;
        if (!unknown && (file.StoredLengthBytes != file.SizeBytes || file.StoredEvidenceVersion != 1
            || file.StoredEvidenceAtUtc is null || hash != hash.ToUpperInvariant()))
            throw Failure(PublicExperienceRequirementPreparationErrors.FileIntegrity);
        return hash.ToUpperInvariant();
    }

    private static PublicExperienceRequirementPreparationException Failure(string code) => new(code);
}
