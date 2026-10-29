using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.CreateRequirement;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.GetRequirementDetails;

public sealed record DocumentDownloadResult(int Status, DocumentDownloadContent? Content = null);

public sealed class DownloadRequirementDocumentService(GetRequirementDetailsService details,
    IRequirementRepository requirements, IFileStorage storage, IInitialContentCapture capture,
    IVerifiedContentCopy verification, ILogger<DownloadRequirementDocumentService> logger)
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public async Task<DocumentDownloadResult> ExecuteAsync(Guid requirementId, Guid fileId, CancellationToken ct)
    {
        try
        {
            var access = await details.ExecuteAsync(new(requirementId), ct);
            ct.ThrowIfCancellationRequested();
            if (!access.IsSuccess) return new(access.Failure switch
            {
                GetRequirementDetailsFailure.Unauthorized => 401,
                GetRequirementDetailsFailure.InactiveUser => 403,
                GetRequirementDetailsFailure.QueryError => 500,
                _ => 404
            });
            // Re-read exact membership without tracking; a retained physical file is not authority.
            var file = (await requirements.ListFilesByRequirementIdAsync(requirementId, ct))
                .SingleOrDefault(f => f.Id == fileId && f.RequirementId == requirementId);
            if (file is null) return new(404);
            var historical = file.StoredSha256 is null && file.StoredLengthBytes is null
                && file.StoredEvidenceAtUtc is null && file.StoredEvidenceVersion is null;
            if (file.SizeBytes <= 0 || file.SizeBytes > CreateRequirementService.MaximumFileSizeBytes
                || (!historical && (file.StoredEvidenceVersion != 1 || file.StoredEvidenceAtUtc is null
                    || file.StoredEvidenceAtUtc.Value.Offset != TimeSpan.Zero
                    || file.StoredLengthBytes != file.SizeBytes || file.StoredSha256 is null
                    || file.StoredSha256.Length != 64
                    || file.StoredSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))))
                return new(500);

            await Slots.WaitAsync(ct);
            ICapturedContent? copy = null;
            var transferred = false;
            try
            {
                await using (var source = await storage.OpenReadAsync(file.StorageKey, ct))
                    copy = historical
                        ? await capture.CaptureAsync(source, file.SizeBytes, CreateRequirementService.MaximumFileSizeBytes, ct)
                        : await verification.PrepareAsync(source, file.SizeBytes, file.StoredSha256!, CreateRequirementService.MaximumFileSizeBytes, ct);
                ct.ThrowIfCancellationRequested();
                var content = new DocumentDownloadContent(copy, copy.OpenRead(), file.OriginalFileName,
                    file.ContentType, Slots, logger);
                transferred = true;
                return new(200, content);
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
        catch (Exception) when (ct.IsCancellationRequested) { ct.ThrowIfCancellationRequested(); throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            try { logger.LogWarning("Document download unavailable. ErrorType={ErrorType}", error.GetType().Name); }
            catch { /* Diagnostics must not expose or replace the safe result. */ }
            return new(500);
        }
    }
}

public sealed class DocumentDownloadContent : IAsyncDisposable
{
    private ICapturedContent? _copy;
    private readonly SemaphoreSlim _slots;
    private readonly ILogger _logger;
    internal DocumentDownloadContent(ICapturedContent copy, Stream reader, string name, string mime,
        SemaphoreSlim slots, ILogger logger)
    {
        _copy = copy; Reader = reader; Length = copy.Length; _slots = slots; _logger = logger;
        FileName = SafeName(name);
        ContentType = mime.ToLowerInvariant() switch
        {
            "application/pdf" => "application/pdf",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            _ => "application/octet-stream"
        };
    }
    public Stream Reader { get; }
    public long Length { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public static string SafeName(string? name)
    {
        var leaf = (name ?? "").Replace('\\', '/').Split('/').Last();
        var safe = new string(leaf.Where(c => !char.IsControl(c) && !"<>:\"|?*".Contains(c)
            && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(180).ToArray()).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "documento" : safe;
    }
    public async ValueTask DisposeAsync()
    {
        var copy = Interlocked.Exchange(ref _copy, null);
        if (copy is null) return;
        try { try { await Reader.DisposeAsync(); } finally { await copy.DisposeAsync(); } }
        catch (Exception error)
        {
            try { _logger.LogWarning("Document download cleanup failed. ErrorType={ErrorType}", error.GetType().Name); }
            catch { /* Preserve the primary transport error. */ }
        }
        finally { _slots.Release(); }
    }
}
