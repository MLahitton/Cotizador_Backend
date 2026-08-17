using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2DocumentProcessingClientTests
{
    private const string Pdf = "application/pdf";
    private const string Xlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Theory]
    [InlineData(Pdf, "planos.pdf")]
    [InlineData(Xlsx, "cuadro.xlsx")]
    [InlineData("image/jpeg", "detalle.jpg")]
    [InlineData("image/png", "detalle.png")]
    public async Task ProcessAsync_SendsSupportedFileAsFilesArray(
        string contentType,
        string fileName)
    {
        var handler = new CaptureHandler(CreatePayload());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var client = CreateClient(httpClient);
        var request = CreateRequest(
            [new DocumentProcessingFile(
                Guid.NewGuid(), fileName, contentType, 3,
                new MemoryStream([1, 2, 3]))]);

        var result = await client.ProcessAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("/requirements/extract", handler.RequestUri?.AbsolutePath);
        Assert.Contains("name=files", handler.MultipartBody);
        Assert.DoesNotContain("name=\"files[]\"", handler.MultipartBody);
        Assert.Contains($"filename={fileName}", handler.MultipartBody);
        Assert.Contains(contentType, handler.MultipartBody);
    }

    [Fact]
    public async Task ProcessAsync_WithMultipleFiles_SendsOneMultipartRequest()
    {
        var handler = new CaptureHandler(CreatePayload());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var client = CreateClient(httpClient);
        var request = CreateRequest(
        [
            new DocumentProcessingFile(
                Guid.NewGuid(), "planos.pdf", Pdf, 3,
                new MemoryStream([1, 2, 3])),
            new DocumentProcessingFile(
                Guid.NewGuid(), "cuadro.xlsx", Xlsx, 2,
                new MemoryStream([4, 5])),
            new DocumentProcessingFile(
                Guid.NewGuid(), "detalle.png", "image/png", 2,
                new MemoryStream([6, 7]))
        ]);

        var result = await client.ProcessAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(3, Count(handler.MultipartBody, "name=files"));
        Assert.DoesNotContain("name=\"files[]\"", handler.MultipartBody);
        Assert.Contains("name=project_id", handler.MultipartBody);
        Assert.Contains("name=requirement_id", handler.MultipartBody);
    }

    [Fact]
    public async Task ProcessAsync_WithMalformedJson_ReturnsInvalidResponse()
    {
        var handler = new CaptureHandler("{");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var logger = new CapturingLogger();
        var client = CreateClient(httpClient, logger);

        var result = await client.ProcessAsync(
            CreateRequest(
            [
                new DocumentProcessingFile(
                    Guid.NewGuid(), "planos.pdf", Pdf, 1,
                    new MemoryStream([1]))
            ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DocumentProcessingClientFailure.InvalidResponse, result.Failure);
        Assert.IsType<InvalidDataException>(logger.Exception);
        Assert.Contains("response_adaptation", logger.Message);
        Assert.Contains("Cotizador_AI2 devolvio JSON invalido", logger.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithRealisticAi2PdfResponse_ReturnsSuccess()
    {
        var handler = new CaptureHandler(
            Ai2RequirementExtractionPayloads.RealisticPdf);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var client = CreateClient(httpClient);

        var result = await client.ProcessAsync(
            CreateRequest(
            [
                new DocumentProcessingFile(
                    Guid.NewGuid(), "requerimiento.pdf", Pdf, 3,
                    new MemoryStream([1, 2, 3]))
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            DocumentProcessingOutcome.RequiresReview,
            result.Response?.Outcome);
        Assert.Equal(4, result.Response?.StructuredExtraction?.Items.Count);
        Assert.Equal(1, handler.CallCount);
    }

    private static CotizadorAi2DocumentProcessingClient CreateClient(
        HttpClient httpClient,
        ILogger<CotizadorAi2DocumentProcessingClient>? logger = null) => new(
        httpClient,
        new CotizadorAi2Options(
            new Uri("http://localhost/"), 30, 1_000_000, true),
        new Ai2RequirementExtractionAdapter(),
        logger ?? NullLogger<CotizadorAi2DocumentProcessingClient>.Instance);

    private static DocumentProcessingClientRequest CreateRequest(
        IReadOnlyList<DocumentProcessingFile> files) => new(
        files[0].DocumentId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        files,
        Guid.NewGuid(),
        Guid.NewGuid());

    private static string CreatePayload() =>
        """
        {
          "requirement": {},
          "sources": [],
          "elements": [],
          "evidence": [],
          "relationships": [],
          "conflicts": [],
          "warnings": [],
          "extraction_metadata": {
            "schema_version": "1.0",
            "source_count": 1,
            "element_count": 0,
            "partial": false,
            "status": "completed",
            "pipeline_version": "ai2-v1"
          }
        }
        """;

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class CaptureHandler(string payload) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string MultipartBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            MultipartBody = await request.Content!.ReadAsStringAsync(
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json"))
            };
        }
    }

    private sealed class CapturingLogger :
        ILogger<CotizadorAi2DocumentProcessingClient>
    {
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
