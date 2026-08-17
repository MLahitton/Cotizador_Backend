using Application.Common.Abstractions.DocumentProcessing;
using Infrastructure.DocumentProcessing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingProviderClientTests
{
    [Fact]
    public async Task ProcessAsync_Ai2TechnicalFailure_UsesConfiguredLegacyFallback()
    {
        var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
        var legacy = Substitute.For<ILegacyDocumentProcessingClient>();
        ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable));
        legacy.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout));
        var client = new DocumentProcessingProviderClient(
            ai2,
            legacy,
            new CotizadorAi2Options(new Uri("http://localhost/"), 30, 1000, true),
            new DocumentProcessingOptions(
                DocumentProcessingProviderKind.Ai2,
                EnableLegacyFallback: true));

        var result = await client.ProcessAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Equal(DocumentProcessingClientFailure.Timeout, result.Failure);
        await legacy.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Ai2BusinessRejection_DoesNotUseFallback()
    {
        var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
        var legacy = Substitute.For<ILegacyDocumentProcessingClient>();
        var rejection = DocumentProcessingClientResult.RemoteFailure(
            DocumentProcessingClientFailure.RemoteRejection,
            new DocumentProcessingRemoteError(422, "AI2-1.0", "INVALID", "Invalid"));
        ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(rejection);
        var client = new DocumentProcessingProviderClient(
            ai2,
            legacy,
            new CotizadorAi2Options(new Uri("http://localhost/"), 30, 1000, true),
            new DocumentProcessingOptions(
                DocumentProcessingProviderKind.Ai2,
                EnableLegacyFallback: true));

        var result = await client.ProcessAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Same(rejection, result);
        await legacy.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static DocumentProcessingClientRequest Request()
    {
        var documentId = Guid.NewGuid();
        return new DocumentProcessingClientRequest(
            documentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [new DocumentProcessingFile(
                documentId, "document.pdf", "application/pdf", 1,
                new MemoryStream([1]))]);
    }
}
