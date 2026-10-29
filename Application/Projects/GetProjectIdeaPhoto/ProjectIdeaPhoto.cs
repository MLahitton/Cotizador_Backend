using Application.Common.Abstractions.Storage;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging;

namespace Application.Projects.GetProjectIdeaPhoto;

public interface IProjectIdeaPhotoRepository
{
    Task<PublicExperienceIdeaPhoto?> FindAsync(Guid projectId, Guid clientId, CancellationToken ct);
}

public enum ProjectIdeaPhotoFailure { None, Unauthorized, InactiveUser, NotFound, Unavailable }
public sealed record ProjectIdeaPhotoResult(ProjectIdeaPhotoFailure Failure, ProjectIdeaPhotoContent? Content = null);

public sealed class ProjectIdeaPhotoContent : IAsyncDisposable
{
    private ICapturedContent? _copy;
    private readonly SemaphoreSlim _slots;
    private readonly ILogger _logger;
    internal ProjectIdeaPhotoContent(ICapturedContent copy, Stream reader, string contentType,
        SemaphoreSlim slots, ILogger logger)
    { _copy = copy; Reader = reader; ContentType = contentType; Length = copy.Length; _slots = slots; _logger = logger; }
    public Stream Reader { get; }
    public string ContentType { get; }
    public long Length { get; }

    public async ValueTask DisposeAsync()
    {
        var copy = Interlocked.Exchange(ref _copy, null);
        if (copy is null) return;
        try
        {
            try { await Reader.DisposeAsync(); }
            finally { await copy.DisposeAsync(); }
        }
        catch (Exception error)
        {
            try { _logger.LogWarning("Idea photo cleanup failed. ErrorType={ErrorType}", error.GetType().Name); }
            catch { /* Diagnostics must not hide the response failure. */ }
        }
        finally { _slots.Release(); }
    }
}
