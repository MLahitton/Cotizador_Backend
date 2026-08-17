using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Application.Common.Abstractions.DocumentProcessing;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2DocumentProcessingClient(
    HttpClient httpClient,
    CotizadorAi2Options options,
    Ai2RequirementExtractionAdapter adapter,
    ILogger<CotizadorAi2DocumentProcessingClient> logger)
    : IAi2DocumentProcessingClient
{
    private const string ExtractionPath = "requirements/extract";
    private const string PdfContentType = "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";

    private static readonly HashSet<string> SupportedContentTypes =
        new(StringComparer.Ordinal)
        {
            PdfContentType,
            XlsxContentType,
            JpegContentType,
            PngContentType
        };

    public async Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var stage = "request_validation";

        try
        {
            ValidateRequest(request);
            stage = "http_request";
            using var message = CreateRequest(request);
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            stage = "response_body";
            var payload = await ReadBodyAsync(
                response.Content,
                options.MaximumResponseBytes,
                timeoutSource.Token);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                stage = "response_adaptation";
                return DocumentProcessingClientResult.Success(
                    adapter.Adapt(payload, request));
            }

            if ((int)response.StatusCode >= 500)
            {
                return DocumentProcessingClientResult.RemoteFailure(
                    DocumentProcessingClientFailure.ServiceError,
                    new DocumentProcessingRemoteError(
                        (int)response.StatusCode,
                        "AI2-1.0",
                        "AI2_SERVICE_ERROR",
                        "Cotizador_AI2 no pudo completar la extraccion."));
            }

            return DocumentProcessingClientResult.RemoteFailure(
                DocumentProcessingClientFailure.RemoteRejection,
                new DocumentProcessingRemoteError(
                    (int)response.StatusCode,
                    "AI2-1.0",
                    "AI2_REQUEST_REJECTED",
                    "Cotizador_AI2 rechazo la solicitud."));
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && timeoutSource.IsCancellationRequested)
        {
            logger.LogWarning(
                "AI2 request timed out. TimeoutSeconds={TimeoutSeconds} CorrelationId={CorrelationId} DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId}",
                options.TimeoutSeconds,
                request.CorrelationId,
                request.DocumentId,
                request.ProcessingAttemptId);
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }
        catch (IOException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(
                exception,
                "AI2 response processing failed. Stage={Stage} CorrelationId={CorrelationId} DocumentId={DocumentId} ProcessingAttemptId={ProcessingAttemptId} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}",
                stage,
                request.CorrelationId,
                request.DocumentId,
                request.ProcessingAttemptId,
                exception.GetType().Name,
                exception.Message);
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
    }

    private static void ValidateRequest(DocumentProcessingClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId == Guid.Empty
            || request.ProcessingAttemptId == Guid.Empty
            || request.CorrelationId == Guid.Empty
            || request.Files is null
            || request.Files.Count == 0)
        {
            throw new InvalidDataException();
        }

        foreach (var file in request.Files)
        {
            if (file.DocumentId == Guid.Empty
                || string.IsNullOrWhiteSpace(file.FileName)
                || !SupportedContentTypes.Contains(file.ContentType)
                || file.SizeBytes <= 0
                || file.Content is null
                || !file.Content.CanRead)
            {
                throw new InvalidDataException();
            }
        }
    }

    private static HttpRequestMessage CreateRequest(
        DocumentProcessingClientRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, ExtractionPath);
        message.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add(
            "X-Correlation-ID",
            request.CorrelationId.ToString("D"));

        var multipart = new MultipartFormDataContent();
        if (request.ProjectId is { } projectId)
        {
            multipart.Add(
                new StringContent(projectId.ToString("D"), Encoding.UTF8),
                "project_id");
        }

        if (request.RequirementId is { } requirementId)
        {
            multipart.Add(
                new StringContent(requirementId.ToString("D"), Encoding.UTF8),
                "requirement_id");
        }

        foreach (var file in request.Files)
        {
            var content = new StreamContent(new NonDisposingStream(file.Content));
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            multipart.Add(content, "files", file.FileName);
        }

        message.Content = multipart;
        return message;
    }

    private static async Task<string> ReadBodyAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Cotizador_AI2 excedio el limite de respuesta de {maximumBytes} bytes.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
