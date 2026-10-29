using System.Security.Cryptography;
using Application.Common.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Storage;

internal static class VerifiedContentIO
{
    internal static void Validate(Stream source, long expectedLength, long maximumLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A readable source is required.", nameof(source));
        if (expectedLength <= 0) throw new ArgumentOutOfRangeException(nameof(expectedLength));
        if (maximumLength <= 0 || expectedLength > maximumLength)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
    }

    internal static async Task<(string Sha256, long Length)> CopyAsync(Stream source, Stream destination,
        long expectedLength, long maximumLength, CancellationToken ct)
    {
        var buffer = new byte[81920];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Read at most one byte beyond the allowed size, even from an unbounded source.
            var remaining = Math.Min(expectedLength, maximumLength) - length;
            var count = remaining >= buffer.Length ? buffer.Length : (int)remaining + 1;
            var read = await source.ReadAsync(buffer.AsMemory(0, count), ct);
            if (read == 0) break;
            if (read > remaining) throw new ContentVerificationException();
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.AppendData(buffer.AsSpan(0, read));
            length += read;
        }
        ct.ThrowIfCancellationRequested();
        if (length == 0 || length != expectedLength) throw new ContentVerificationException();
        return (Convert.ToHexString(hash.GetHashAndReset()), length);
    }

    internal static void DeleteFile(string path, ILogger logger)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning("Verified content cleanup failed. Resource=File ErrorType={ErrorType}", e.GetType().Name);
        }
    }

    internal static void DeleteDirectory(string path, ILogger logger)
    {
        try { Directory.Delete(path, recursive: false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning("Verified content cleanup failed. Resource=Directory ErrorType={ErrorType}", e.GetType().Name);
        }
    }
}
