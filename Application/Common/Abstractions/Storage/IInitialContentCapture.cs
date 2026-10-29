namespace Application.Common.Abstractions.Storage;

public interface IInitialContentCapture
{
    Task<ICapturedContent> CaptureAsync(Stream source, long declaredLength, long maximumLength,
        CancellationToken cancellationToken);
}

// Observed bytes, not verification against previous evidence. The caller owns the source.
public interface ICapturedContent : IAsyncDisposable
{
    string Sha256 { get; }
    long Length { get; }
    Stream OpenRead();
}
