using Application.Common.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Storage;

public sealed class InitialContentCapture(ILogger<InitialContentCapture> logger) : IInitialContentCapture
{
    public async Task<ICapturedContent> CaptureAsync(Stream source, long declaredLength, long maximumLength,
        CancellationToken cancellationToken) =>
        await SealedContentCopy.CaptureInitialAsync(source, declaredLength, maximumLength, logger, cancellationToken);
}
