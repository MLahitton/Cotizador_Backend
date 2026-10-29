using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using Domain.Identity;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging;

namespace Application.Projects.GetProjectIdeaPhoto;

public sealed class GetProjectIdeaPhotoService(ICurrentUser actor, IIdentityRepository users,
    IProjectRepository projects, IClientRepository clients, IProjectIdeaPhotoRepository photos,
    IFileStorage storage, IVerifiedContentCopy copies, ILogger<GetProjectIdeaPhotoService> logger)
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public async Task<ProjectIdeaPhotoResult> ExecuteAsync(Guid projectId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!actor.IsAuthenticated || actor.UserId is not Guid userId)
                return new(ProjectIdeaPhotoFailure.Unauthorized);
            var user = await users.FindUserByIdAsync(userId, ct);
            if (user is null) return new(ProjectIdeaPhotoFailure.Unauthorized);
            if (!user.IsActive) return new(ProjectIdeaPhotoFailure.InactiveUser);
            var project = await projects.FindByIdAsync(projectId, ct);
            if (project is null || (user.Role != UserRole.Admin && project.CreatedByUserId != userId))
                return new(ProjectIdeaPhotoFailure.NotFound);
            if (await clients.FindByIdAsync(project.ClientId, ct) is null)
                return new(ProjectIdeaPhotoFailure.NotFound);
            var photo = await photos.FindAsync(project.Id, project.ClientId, ct);
            ct.ThrowIfCancellationRequested();
            if (photo is null) return new(ProjectIdeaPhotoFailure.None);
            Validate(photo);

            await Slots.WaitAsync(ct);
            ICapturedContent? copy = null;
            var transferred = false;
            try
            {
                await using (var source = await storage.OpenReadAsync(photo.StorageKey, ct))
                    copy = await copies.PrepareAsync(source, photo.StoredLengthBytes, photo.StoredSha256,
                        IdeaPhotoLimits.MaximumBytes, ct);
                ct.ThrowIfCancellationRequested();
                var content = new ProjectIdeaPhotoContent(copy, copy.OpenRead(), photo.ContentType, Slots, logger);
                transferred = true;
                return new(ProjectIdeaPhotoFailure.None, content);
            }
            finally
            {
                if (!transferred)
                {
                    try { if (copy is not null) await copy.DisposeAsync(); }
                    finally { Slots.Release(); }
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        { ct.ThrowIfCancellationRequested(); throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            // The HTTP request scope supplies correlation; never log provider paths or SQL.
            logger.LogWarning("Project idea photo unavailable. ProjectId={ProjectId} ErrorType={ErrorType}",
                projectId, error.GetType().Name);
            return new(ProjectIdeaPhotoFailure.Unavailable);
        }
    }

    private static void Validate(PublicExperienceIdeaPhoto photo)
    {
        if (photo.IdentityVersion != 1 || photo.StoredEvidenceVersion != 1
            || photo.Id == Guid.Empty || photo.SubmissionId == Guid.Empty
            || photo.ContentType is not ("image/jpeg" or "image/png")
            || photo.StoredLengthBytes <= 0 || photo.StoredLengthBytes > IdeaPhotoLimits.MaximumBytes
            || string.IsNullOrWhiteSpace(photo.StorageKey)
            || photo.StoredSha256 is null || photo.StoredSha256.Length != 64
            || photo.StoredSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("Invalid photo evidence.");
    }
}
