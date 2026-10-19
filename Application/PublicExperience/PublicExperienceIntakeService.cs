using System.Security.Cryptography;
using System.Text.Json;
using Application.Common.Abstractions.Storage;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging;

namespace Application.PublicExperience;

public sealed class PublicExperienceIntakeService(
    IPublicExperienceSubmissionRepository repository,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<PublicExperienceIntakeService> logger)
{
    public async Task<PublicExperienceIntakeResult> ExecuteAsync(
        string json, IReadOnlyList<PublicExperienceUpload> uploads, CancellationToken cancellationToken)
    {
        PublicExperiencePayload payload;
        var hashes = new List<string>();
        try
        {
            payload = PublicExperiencePayloadValidation.Parse(json, uploads);
            foreach (var upload in uploads)
                hashes.Add(await HashFileAsync(upload, cancellationToken));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new(400, Error: exception.Message);
        }

        using var fingerprintHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprintHash.AppendData(PublicExperiencePayloadValidation.Canonicalize(payload));
        foreach (var hash in hashes)
            fingerprintHash.AppendData(Convert.FromHexString(hash));
        var fingerprint = Convert.ToHexString(fingerprintHash.GetHashAndReset());
        var existing = await repository.FindAsync(payload.SubmissionId, cancellationToken);
        if (existing is not null) return Replay(existing, fingerprint);

        var now = timeProvider.GetUtcNow();
        var submission = new PublicExperienceSubmission
        {
            SubmissionId = payload.SubmissionId,
            SchemaVersion = payload.SchemaVersion,
            Source = payload.Source,
            ClientFullName = payload.Client.FullName,
            ClientWhatsapp = payload.Client.Whatsapp,
            AdvisorCode = payload.Client.AdvisorCode,
            AdvisorDisplayName = payload.Client.AdvisorDisplayName,
            ProjectName = payload.Project.Name,
            ProjectCity = payload.Project.City,
            ProjectSubdivision = payload.Project.Subdivision,
            ProjectLotOrApartment = payload.Project.LotOrApartment,
            ProjectNotes = payload.Project.Notes,
            AnswersJson = payload.Answers.GetRawText(),
            PayloadFingerprint = fingerprint,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var storedKeys = new List<string>();
        try
        {
            for (var index = 0; index < uploads.Count; index++)
            {
                var upload = uploads[index];
                var fileId = Guid.NewGuid();
                var key = $"public-experience/{submission.Id:N}/{fileId:N}/original{Path.GetExtension(upload.FileName).ToLowerInvariant()}";
                // Include the current key so even a partially written file is compensated.
                storedKeys.Add(key);
                await using var content = upload.OpenRead();
                await storage.SaveAsync(key, content, cancellationToken);
                submission.Files.Add(new PublicExperienceSubmissionFile
                {
                    Id = fileId, SubmissionId = submission.Id, Sequence = index,
                    OriginalFileName = upload.FileName, ContentType = upload.ContentType,
                    SizeBytes = upload.SizeBytes, StorageKey = key, ContentHash = hashes[index],
                    CreatedAtUtc = now
                });
            }
            if (await repository.TryInsertAsync(submission, cancellationToken))
                return new(201, submission.SubmissionId, submission.Status);
        }
        catch
        {
            await CompensateAsync(storedKeys);
            throw;
        }

        // A competing insert won the unique constraint. Only remove this request's unique keys.
        await CompensateAsync(storedKeys);
        existing = await repository.FindAsync(payload.SubmissionId, cancellationToken)
            ?? throw new InvalidOperationException("Concurrent submission was not found.");
        return Replay(existing, fingerprint);
    }

    private static PublicExperienceIntakeResult Replay(PublicExperienceSubmission existing, string fingerprint) =>
        existing.PayloadFingerprint == fingerprint
            ? new(200, existing.SubmissionId, existing.Status, true)
            : new(409, Error: "submissionId ya fue recibido con contenido diferente.");

    private static async Task<string> HashFileAsync(PublicExperienceUpload upload, CancellationToken cancellationToken)
    {
        await using var stream = upload.OpenRead();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            length += read;
            if (length > PublicExperiencePayloadValidation.MaximumFileBytes || length > upload.SizeBytes)
                throw new ArgumentException("Tamano real del archivo no coincide con metadata.");
            hash.AppendData(buffer.AsSpan(0, read));
        }
        if (length == 0 || length != upload.SizeBytes)
            throw new ArgumentException("Tamano real del archivo no coincide con metadata.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task CompensateAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { await storage.DeleteIfExistsAsync(key, CancellationToken.None); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Public intake compensation failed. StorageKey={StorageKey}", key);
            }
        }
    }
}
