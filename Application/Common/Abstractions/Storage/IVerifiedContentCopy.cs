namespace Application.Common.Abstractions.Storage;

public interface IVerifiedContentCopy
{
    Task<ICapturedContent> PrepareAsync(Stream source, long expectedLength, string expectedSha256,
        long maximumLength, CancellationToken cancellationToken);
}
