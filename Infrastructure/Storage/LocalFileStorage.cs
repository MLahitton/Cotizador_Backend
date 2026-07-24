using Application.Common.Abstractions.Storage;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string rootPath;
    private readonly string rootPathWithSeparator;
    private readonly StringComparison pathComparison;

    public LocalFileStorage(
        FileStorageOptions options,
        IHostEnvironment hostEnvironment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        rootPath = Path.GetFullPath(
            Path.IsPathRooted(options.RootPath)
                ? options.RootPath
                : Path.Combine(
                    hostEnvironment.ContentRootPath,
                    options.RootPath));

        rootPathWithSeparator = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        var destinationPath = ResolveStoragePath(storageKey);

        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead)
        {
            throw new ArgumentException(
                "El stream de contenido debe permitir lectura.",
                nameof(content));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var destinationDirectory =
            Path.GetDirectoryName(destinationPath)!;
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationPath))
            {
                throw new FileStorageConflictException();
            }

            temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Guid.NewGuid():N}.tmp");

            await using (var temporaryStream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                        | FileOptions.SequentialScan
                }))
            {
                await content.CopyToAsync(
                    temporaryStream,
                    cancellationToken);

                await temporaryStream.FlushAsync(cancellationToken);
            }

            try
            {
                File.Move(
                    temporaryPath,
                    destinationPath,
                    overwrite: false);
            }
            catch (IOException exception)
                when (File.Exists(destinationPath))
            {
                throw new FileStorageConflictException(exception);
            }
        }
        catch (InvalidStorageKeyException)
        {
            throw;
        }
        catch (FileStorageConflictException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new FileStorageWriteException(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileStorageWriteException(exception);
        }
        catch (NotSupportedException exception)
        {
            throw new FileStorageWriteException(exception);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storagePath = ResolveStoragePath(storageKey);

        try
        {
            Stream stream = new FileStream(
                storagePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous
                        | FileOptions.SequentialScan
                });

            return Task.FromResult(stream);
        }
        catch (IOException exception)
        {
            throw new FileStorageReadException(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileStorageReadException(exception);
        }
        catch (NotSupportedException exception)
        {
            throw new FileStorageReadException(exception);
        }
    }

    public Task DeleteIfExistsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storagePath = ResolveStoragePath(storageKey);

        try
        {
            if (!File.Exists(storagePath))
            {
                return Task.CompletedTask;
            }

            File.Delete(storagePath);

            return Task.CompletedTask;
        }
        catch (IOException exception)
        {
            throw new FileStorageDeleteException(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new FileStorageDeleteException(exception);
        }
        catch (NotSupportedException exception)
        {
            throw new FileStorageDeleteException(exception);
        }
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || !string.Equals(
                storageKey,
                storageKey.Trim(),
                StringComparison.Ordinal)
            || Path.IsPathRooted(storageKey)
            || storageKey.Contains('\\')
            || storageKey.StartsWith(
                "/",
                StringComparison.Ordinal)
            || storageKey.EndsWith(
                "/",
                StringComparison.Ordinal))
        {
            throw new InvalidStorageKeyException();
        }

        var segments = storageKey.Split(
            '/',
            StringSplitOptions.None);

        if (segments.Any(IsInvalidSegment))
        {
            throw new InvalidStorageKeyException();
        }

        string resolvedPath;

        try
        {
            resolvedPath = Path.GetFullPath(
                Path.Combine(
                    rootPath,
                    Path.Combine(segments)));
        }
        catch (ArgumentException)
        {
            throw new InvalidStorageKeyException();
        }
        catch (NotSupportedException)
        {
            throw new InvalidStorageKeyException();
        }
        catch (PathTooLongException)
        {
            throw new InvalidStorageKeyException();
        }

        if (string.Equals(
                resolvedPath,
                rootPath,
                pathComparison)
            || !resolvedPath.StartsWith(
                rootPathWithSeparator,
                pathComparison))
        {
            throw new InvalidStorageKeyException();
        }

        return resolvedPath;
    }

    private static bool IsInvalidSegment(string segment)
    {
        return segment.Length == 0
            || segment is "." or ".." or "..."
            || segment.Contains(
                ':',
                StringComparison.Ordinal)
            || segment.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0
            || segment.Any(character =>
                char.IsControl(character)
                || character is '<' or '>' or '"' or '|'
                    or '?' or '*');
    }

    private static void TryDeleteTemporaryFile(string? temporaryPath)
    {
        if (temporaryPath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
