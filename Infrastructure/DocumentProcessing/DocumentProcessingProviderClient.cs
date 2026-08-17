using Application.Common.Abstractions.DocumentProcessing;

namespace Infrastructure.DocumentProcessing;

public interface ILegacyDocumentProcessingClient : IDocumentProcessingClient;

public sealed class DocumentProcessingProviderClient(
    IAi2DocumentProcessingClient ai2Client,
    ILegacyDocumentProcessingClient legacyClient,
    CotizadorAi2Options ai2Options,
    DocumentProcessingOptions options)
    : IDocumentProcessingClient
{
    public async Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        if (options.Provider == DocumentProcessingProviderKind.LegacyAi)
        {
            return await legacyClient.ProcessAsync(request, cancellationToken);
        }

        if (!ai2Options.Enabled)
        {
            return options.EnableLegacyFallback
                ? await legacyClient.ProcessAsync(request, cancellationToken)
                : DocumentProcessingClientResult.Failed(
                    DocumentProcessingClientFailure.ServiceUnavailable);
        }

        var result = await ai2Client.ProcessAsync(request, cancellationToken);
        return options.EnableLegacyFallback && IsTechnicalFailure(result.Failure)
            ? await legacyClient.ProcessAsync(request, cancellationToken)
            : result;
    }

    private static bool IsTechnicalFailure(
        DocumentProcessingClientFailure failure) => failure is
        DocumentProcessingClientFailure.ServiceUnavailable
        or DocumentProcessingClientFailure.Timeout
        or DocumentProcessingClientFailure.ServiceError
        or DocumentProcessingClientFailure.InvalidResponse;
}
