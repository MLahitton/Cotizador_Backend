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
    ILogger<PublicExperienceIntakeService> logger,
    IInitialContentCapture capture,
    IIdeaPhotoCapture? photoCapture = null)
{
    public Task<PublicExperienceIntakeResult> ExecuteAsync(
        string json, IReadOnlyList<PublicExperienceUpload> uploads, CancellationToken cancellationToken) =>
        ExecuteAsync(json, uploads, null, cancellationToken);

    public async Task<PublicExperienceIntakeResult> ExecuteAsync(
        string json, IReadOnlyList<PublicExperienceUpload> uploads, PublicExperienceUpload? ideaPhoto,
        CancellationToken cancellationToken)
    {
        var captures = new List<ICapturedContent>();
        IValidatedIdeaPhoto? photo = null;
        try
        {
            PublicExperiencePayload payload;
            try
            {
                payload = PublicExperiencePayloadValidation.Parse(json, uploads);
                if (ideaPhoto is not null)
                    photo = await (photoCapture ?? throw new InvalidOperationException("Photo capture is not configured."))
                        .CaptureAsync(ideaPhoto, cancellationToken);
                foreach (var upload in uploads)
                {
                    await using var source = upload.OpenRead();
                    captures.Add(await capture.CaptureAsync(source, upload.SizeBytes,
                        PublicExperiencePayloadValidation.MaximumFileBytes, cancellationToken));
                }
            }
            catch (IdeaPhotoValidationException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return new(exception.StatusCode, Error: exception.Message, ErrorCode: exception.Code);
            }
            catch (ContentVerificationException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(400, Error: "Tamano real del archivo no coincide con metadata.");
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return new(400, Error: exception.Message);
            }
            return await PersistAsync(payload, uploads, captures, ideaPhoto, photo, cancellationToken);
        }
        finally
        {
            try { foreach (var content in captures) await content.DisposeAsync(); }
            finally { if (photo is not null) await photo.DisposeAsync(); }
        }
    }

    private async Task<PublicExperienceIntakeResult> PersistAsync(PublicExperiencePayload payload,
        IReadOnlyList<PublicExperienceUpload> uploads, IReadOnlyList<ICapturedContent> captures,
        PublicExperienceUpload? ideaPhoto, IValidatedIdeaPhoto? photo,
        CancellationToken cancellationToken)
    {
        using var fingerprintHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprintHash.AppendData(PublicExperiencePayloadValidation.Canonicalize(payload));
        foreach (var content in captures)
            fingerprintHash.AppendData(Convert.FromHexString(content.Sha256));
        var fingerprint = Convert.ToHexString(fingerprintHash.GetHashAndReset());
        var existing = await repository.FindAsync(payload.SubmissionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (existing is not null) return Replay(existing, fingerprint, photo);

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
                // Track attempted destinations, not proof of creation or permission to delete them.
                storedKeys.Add(key);
                var captured = captures[index];
                await using var content = captured.OpenRead();
                var receipt = await storage.SaveVerifiedAsync(key, content, upload.SizeBytes,
                    PublicExperiencePayloadValidation.MaximumFileBytes, cancellationToken);
                var evidenceAt = timeProvider.GetUtcNow();
                if (receipt.Sha256 != captured.Sha256 || receipt.Length != captured.Length)
                    throw new FileStorageWriteException(new ContentVerificationException());
                submission.Files.Add(PublicExperienceSubmissionFile.CreateWithStoredEvidence(
                    fileId, submission.Id, index, upload.FileName, upload.ContentType,
                    upload.SizeBytes, key, receipt.Sha256, now, receipt.Length, evidenceAt, 1));
            }
            if (photo is not null)
            {
                var id = Guid.NewGuid();
                var extension = photo.ContentType == "image/png" ? ".png" : ".jpg";
                var key = $"public-experience/{submission.Id:N}/idea-photo/{id:N}/original{extension}";
                storedKeys.Add(key);
                await using var content = photo.OpenRead();
                var receipt = await storage.SaveVerifiedAsync(key, content, photo.Length,
                    IdeaPhotoLimits.MaximumBytes, cancellationToken);
                if (receipt.Sha256 != photo.Sha256 || receipt.Length != photo.Length)
                    throw new FileStorageWriteException(new ContentVerificationException());
                submission.AttachIdeaPhoto(PublicExperienceIdeaPhoto.Create(id, submission.Id,
                    ideaPhoto!.FileName, photo.ContentType, key, receipt.Sha256, receipt.Length, timeProvider.GetUtcNow()));
            }
            if (await repository.TryInsertAsync(submission, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(201, submission.SubmissionId, submission.Status);
            }
        }
        catch
        {
            logger.LogWarning("Public intake failed; attempted files retained. SubmissionId={SubmissionId} FileCount={FileCount}",
                payload.SubmissionId, storedKeys.Count);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        // The winner remains authoritative. Losing upload bytes await coordinated cleanup.
        existing = await repository.FindAsync(payload.SubmissionId, cancellationToken)
            ?? throw new InvalidOperationException("Concurrent submission was not found.");
        cancellationToken.ThrowIfCancellationRequested();
        return Replay(existing, fingerprint, photo);
    }

    private static PublicExperienceIntakeResult Replay(PublicExperienceSubmission existing, string fingerprint,
        IValidatedIdeaPhoto? photo) =>
        existing.PayloadFingerprint == fingerprint
            && (photo is null ? existing.IdeaPhoto is null : existing.IdeaPhoto is { IdentityVersion: 1 } stored
                && stored.StoredSha256 == photo.Sha256 && stored.StoredLengthBytes == photo.Length)
            ? new(200, existing.SubmissionId, existing.Status, true)
            : new(409, Error: "submissionId ya fue recibido con contenido diferente.");

}
