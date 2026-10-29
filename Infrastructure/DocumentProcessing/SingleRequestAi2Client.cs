using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DocumentProcessing;

// Scoped in the Experience pipeline; no redirect, cookie replay or retry handler.
public sealed class SingleRequestAi2Client : IVerifiedExecutionDocumentClient, IDisposable
{
    private readonly HttpClient http;
    private readonly CotizadorAi2DocumentProcessingClient inner;

    public SingleRequestAi2Client(CotizadorAi2Options options, Ai2RequirementExtractionAdapter adapter,
        ILogger<CotizadorAi2DocumentProcessingClient> logger)
        : this(options, adapter, logger, new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { }

    internal SingleRequestAi2Client(CotizadorAi2Options options, Ai2RequirementExtractionAdapter adapter,
        ILogger<CotizadorAi2DocumentProcessingClient> logger, HttpMessageHandler handler)
    {
        http = new HttpClient(handler) { BaseAddress = options.BaseUri, Timeout = Timeout.InfiniteTimeSpan };
        inner = new(http, options, adapter, logger, captureRawResponse: false);
    }

    public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request,
        CancellationToken cancellationToken) => inner.ProcessAsync(request, cancellationToken);

    public void Dispose() => http.Dispose();
}
