using System.Security.Cryptography;
using Application.PublicExperience;
using ImageMagick;

namespace Infrastructure.Storage;

public sealed class IdeaPhotoCapture : IIdeaPhotoCapture
{
    // The lease covers capture through disposal, not just the native decode.
    private static readonly SemaphoreSlim Slots = new(2, 2);

    static IdeaPhotoCapture()
    {
        ResourceLimits.Width = IdeaPhotoLimits.MaximumDimension;
        ResourceLimits.Height = IdeaPhotoLimits.MaximumDimension;
        // Native cache thresholds are separate from the explicit pixel-count guard.
        ResourceLimits.Area = 128UL * 1024 * 1024;
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.ListLength = 2;
        ResourceLimits.Thread = 1;
        ResourceLimits.MaxProfileSize = (ulong)IdeaPhotoLimits.MaximumBytes;
    }

    public async Task<IValidatedIdeaPhoto> CaptureAsync(PublicExperienceUpload upload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { PublicExperiencePayloadValidation.ValidateFileName(upload.FileName); }
        catch (ArgumentException) { throw Invalid(); }
        if (upload.SizeBytes <= 0) throw Invalid();
        if (upload.SizeBytes > IdeaPhotoLimits.MaximumBytes)
            throw new IdeaPhotoValidationException(413, "PUBLIC_EXPERIENCE_PHOTO_TOO_LARGE");
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png")
            || upload.ContentType is not ("" or "application/octet-stream" or "image/jpeg" or "image/png"))
            throw Unsupported();
        await Slots.WaitAsync(ct);
        var transferred = false;
        try
        {
            var bytes = new byte[checked((int)upload.SizeBytes)];
            await using (var source = upload.OpenRead())
            {
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var count = await source.ReadAsync(bytes.AsMemory(offset), ct);
                    if (count == 0) throw Invalid();
                    offset += count;
                }
                if (await source.ReadAsync(new byte[1], ct) != 0)
                    throw upload.SizeBytes == IdeaPhotoLimits.MaximumBytes
                        ? new IdeaPhotoValidationException(413, "PUBLIC_EXPERIENCE_PHOTO_TOO_LARGE") : Invalid();
            }
            // Signature selects a restricted codec; it is not content validation.
            var format = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                ? MagickFormat.Png : bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 })
                    ? MagickFormat.Jpeg : throw Invalid();
            var mime = format == MagickFormat.Png ? "image/png" : "image/jpeg";
            if ((extension == ".png") != (format == MagickFormat.Png)
                || (upload.ContentType is "image/jpeg" or "image/png" && upload.ContentType != mime))
                throw Unsupported();
            ct.ThrowIfCancellationRequested();
            try
            {
                using var headers = new MagickImageCollection();
                var headerWarning = false;
                headers.Warning += (_, _) => headerWarning = true;
                headers.Ping(bytes, new MagickReadSettings { Format = format });
                ct.ThrowIfCancellationRequested();
                if (headerWarning || headers.Count != 1
                    || headers[0].Width > IdeaPhotoLimits.MaximumDimension || headers[0].Height > IdeaPhotoLimits.MaximumDimension
                    || (long)headers[0].Width * headers[0].Height > IdeaPhotoLimits.MaximumPixels
                    || headers[0].GetAttribute("png:acTL") is not null)
                    throw Invalid();
                using var decoded = new MagickImage();
                var warned = false;
                decoded.Warning += (_, _) => warned = true;
                decoded.Progress += (_, progress) => progress.Cancel = ct.IsCancellationRequested;
                decoded.Read(bytes, new MagickReadSettings { Format = format });
                ct.ThrowIfCancellationRequested();
                if (warned || decoded.Width == 0 || decoded.Height == 0
                    || decoded.Width > IdeaPhotoLimits.MaximumDimension || decoded.Height > IdeaPhotoLimits.MaximumDimension
                    || (long)decoded.Width * decoded.Height > IdeaPhotoLimits.MaximumPixels
                    || decoded.GetAttribute("png:acTL") is not null)
                    throw Invalid();
            }
            catch (MagickException)
            {
                ct.ThrowIfCancellationRequested();
                throw Invalid();
            }
            ct.ThrowIfCancellationRequested();
            var result = new CapturedPhoto(bytes, mime);
            transferred = true;
            return result;
        }
        finally { if (!transferred) Slots.Release(); }
    }

    private static IdeaPhotoValidationException Invalid() => new(400, "PUBLIC_EXPERIENCE_PHOTO_INVALID");
    private static IdeaPhotoValidationException Unsupported() => new(415, "PUBLIC_EXPERIENCE_PHOTO_UNSUPPORTED");

    private sealed class CapturedPhoto(byte[] content, string contentType) : IValidatedIdeaPhoto
    {
        private byte[]? _content = content;
        public string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(content));
        public long Length { get; } = content.LongLength;
        public string ContentType { get; } = contentType;
        public Stream OpenRead() => new MemoryStream(_content ?? throw new ObjectDisposedException(nameof(CapturedPhoto)), false);
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _content, null) is not null) Slots.Release();
            return ValueTask.CompletedTask;
        }
    }
}
