namespace Application.Common.Abstractions.Storage;

public interface IFileStorage
{
    Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(
        string storageKey,
        CancellationToken cancellationToken);
}

public sealed class InvalidStorageKeyException : Exception
{
    public InvalidStorageKeyException()
        : base("La clave de almacenamiento no es válida.")
    {
    }
}

public sealed class FileStorageConflictException : Exception
{
    public FileStorageConflictException()
        : base(
            "Ya existe un archivo con la clave de almacenamiento indicada.")
    {
    }

    public FileStorageConflictException(Exception innerException)
        : base(
            "Ya existe un archivo con la clave de almacenamiento indicada.",
            innerException)
    {
    }
}

public sealed class FileStorageWriteException : Exception
{
    public FileStorageWriteException(Exception innerException)
        : base(
            "No fue posible escribir el archivo.",
            innerException)
    {
    }
}

public sealed class FileStorageReadException : Exception
{
    public FileStorageReadException(Exception innerException)
        : base(
            "No fue posible leer el archivo.",
            innerException)
    {
    }
}

public sealed class FileStorageDeleteException : Exception
{
    public FileStorageDeleteException(Exception innerException)
        : base(
            "No fue posible eliminar el archivo.",
            innerException)
    {
    }
}
