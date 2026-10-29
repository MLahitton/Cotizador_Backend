using Application.Common.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Storage;

public sealed class VerifiedContentCopy(ILogger<VerifiedContentCopy> logger) : IVerifiedContentCopy
{
    public async Task<ICapturedContent> PrepareAsync(Stream source, long expectedLength, string expectedSha256,
        long maximumLength, CancellationToken cancellationToken) =>
        await SealedContentCopy.PrepareAsync(source, expectedLength, expectedSha256, maximumLength, logger, cancellationToken);
}
