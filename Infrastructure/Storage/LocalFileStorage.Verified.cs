using Application.Common.Abstractions.Storage;

namespace Infrastructure.Storage;

public sealed partial class LocalFileStorage
{
    public async Task<StoredContentReceipt> SaveVerifiedAsync(string storageKey, Stream content,
        long expectedLength, long maximumLength, CancellationToken cancellationToken)
    {
        var destination = ResolveStoragePath(storageKey);
        VerifiedContentIO.Validate(content, expectedLength, maximumLength);
        cancellationToken.ThrowIfCancellationRequested();
        string? temporary = null;
        var owned = false;
        try
        {
            var directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(destination)) throw new FileStorageConflictException();
            temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            (string Sha256, long Length) evidence;
            await using (var target = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            }))
            {
                owned = true;
                evidence = await VerifiedContentIO.CopyAsync(content, target, expectedLength, maximumLength, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException e) when (File.Exists(destination)) { throw new FileStorageConflictException(e); }
            // No cancellation check or destination compensation after promotion.
            return new StoredContentReceipt(evidence.Sha256, evidence.Length);
        }
        catch (IOException e) { throw new FileStorageWriteException(e); }
        catch (UnauthorizedAccessException e) { throw new FileStorageWriteException(e); }
        catch (NotSupportedException e) { throw new FileStorageWriteException(e); }
        finally
        {
            if (owned) VerifiedContentIO.DeleteFile(temporary!, logger);
        }
    }
}
