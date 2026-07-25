using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;

namespace Infrastructure.DocumentProcessing;

public sealed class CotizadorAiDocumentProcessingClient(
    HttpClient httpClient,
    CotizadorAiOptions options)
    : IDocumentProcessingClient
{
    private const string DocumentExtractionPath =
        "api/v1/prequotes/document-extractions";

    private const string CorrelationHeaderName = "X-Correlation-ID";
    private const long MaximumPdfSizeBytes = 20_971_520;

    private static readonly UTF8Encoding StrictUtf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false
    };

    public async Task<DocumentProcessingClientResult> ProcessAsync(
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutSource.CancelAfter(
            TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var requestMessage = CreateRequestMessage(request);
            using var responseMessage = await httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            return await ProcessResponseAsync(
                responseMessage,
                request,
                timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && timeoutSource.IsCancellationRequested)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout);
        }
        catch (InvalidDataException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (JsonException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }
        catch (DecoderFallbackException)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
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
    }

    private static void ValidateRequest(
        DocumentProcessingClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del documento es obligatorio.",
                nameof(request));
        }

        if (request.ProcessingAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador del intento de procesamiento es obligatorio.",
                nameof(request));
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador de correlación es obligatorio.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException(
                "El nombre del archivo es obligatorio.",
                nameof(request));
        }

        if (!string.Equals(
                request.FileName,
                request.FileName.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "El nombre del archivo no puede contener espacios externos.",
                nameof(request));
        }

        if (request.FileName.Length > 255)
        {
            throw new ArgumentException(
                "El nombre del archivo no puede superar 255 caracteres.",
                nameof(request));
        }

        if (request.SizeBytes <= 0
            || request.SizeBytes > MaximumPdfSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.SizeBytes,
                "El tamaño del archivo debe estar entre 1 y 20971520 bytes.");
        }

        if (request.Content is null)
        {
            throw new ArgumentNullException(
                nameof(request),
                "El contenido del archivo es obligatorio.");
        }

        if (!request.Content.CanRead)
        {
            throw new ArgumentException(
                "El contenido del archivo debe ser legible.",
                nameof(request));
        }
    }

    private static HttpRequestMessage CreateRequestMessage(
        DocumentProcessingClientRequest request)
    {
        var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            DocumentExtractionPath);

        requestMessage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        requestMessage.Headers.Add(
            CorrelationHeaderName,
            request.CorrelationId.ToString("D"));

        var multipartContent = new MultipartFormDataContent();

        multipartContent.Add(
            new StringContent(
                request.DocumentId.ToString("D"),
                Encoding.UTF8),
            "documentId");

        multipartContent.Add(
            new StringContent(
                request.ProcessingAttemptId.ToString("D"),
                Encoding.UTF8),
            "processingAttemptId");

        var fileContent = new StreamContent(
            new NonDisposingStream(request.Content));

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/pdf");

        multipartContent.Add(
            fileContent,
            "file",
            request.FileName);

        requestMessage.Content = multipartContent;

        return requestMessage;
    }

    private async Task<DocumentProcessingClientResult> ProcessResponseAsync(
        HttpResponseMessage response,
        DocumentProcessingClientRequest request,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        ValidateCorrelationHeader(response, request.CorrelationId);

        if (statusCode is 502 or 503)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable);
        }

        if (statusCode is 408 or 504)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.Timeout);
        }

        if (statusCode is not 200 and not 413 and not 415 and not 422 and not 500)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        ValidateContentType(response);

        var payloadJson = await ReadResponseBodyAsync(
            response.Content,
            options.MaximumResponseBytes,
            cancellationToken);

        if (statusCode == 200)
        {
            var responseData = ParseSuccessResponse(
                payloadJson,
                request);

            return DocumentProcessingClientResult.Success(responseData);
        }

        return ParseErrorResponse(payloadJson, statusCode);
    }

    private static void ValidateCorrelationHeader(
        HttpResponseMessage response,
        Guid expectedCorrelationId)
    {
        if (!response.Headers.TryGetValues(
                CorrelationHeaderName,
                out var values))
        {
            throw new InvalidDataException();
        }

        var correlationValues = values.ToArray();

        if (correlationValues.Length != 1
            || string.IsNullOrWhiteSpace(correlationValues[0])
            || !Guid.TryParse(
                correlationValues[0],
                out var correlationId)
            || correlationId == Guid.Empty
            || correlationId != expectedCorrelationId)
        {
            throw new InvalidDataException();
        }
    }

    private static void ValidateContentType(
        HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues(
                "Content-Type",
                out var values))
        {
            throw new InvalidDataException();
        }

        var contentTypeValues = values.ToArray();

        if (contentTypeValues.Length != 1
            || !MediaTypeHeaderValue.TryParse(
                contentTypeValues[0],
                out var contentType)
            || !string.Equals(
                contentType.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException();
        }
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpContent content,
        long maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > maximumResponseBytes)
        {
            throw new InvalidDataException();
        }

        await using var responseStream =
            await content.ReadAsStreamAsync(cancellationToken);

        using var bodyBuffer = new MemoryStream();
        var readBuffer = new byte[81_920];
        var maximumBytesToRead = maximumResponseBytes + 1;
        long totalBytesRead = 0;

        while (totalBytesRead < maximumBytesToRead)
        {
            var remainingBytes = maximumBytesToRead - totalBytesRead;
            var bytesToRead = (int)Math.Min(
                readBuffer.Length,
                remainingBytes);
            var bytesRead = await responseStream.ReadAsync(
                readBuffer.AsMemory(0, bytesToRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            await bodyBuffer.WriteAsync(
                readBuffer.AsMemory(0, bytesRead),
                cancellationToken);

            totalBytesRead += bytesRead;
        }

        if (totalBytesRead == 0
            || totalBytesRead > maximumResponseBytes)
        {
            throw new InvalidDataException();
        }

        return StrictUtf8Encoding.GetString(
            bodyBuffer.GetBuffer(),
            0,
            checked((int)totalBytesRead));
    }

    private DocumentProcessingResponseData ParseSuccessResponse(
        string payloadJson,
        DocumentProcessingClientRequest request)
    {
        using var jsonDocument = JsonDocument.Parse(
            payloadJson,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

        ValidateSuccessJsonShape(jsonDocument.RootElement);

        var response = JsonSerializer.Deserialize<SuccessResponseDto>(
            payloadJson,
            SerializerOptions)
            ?? throw new InvalidDataException();

        if (!string.Equals(
                response.SchemaVersion,
                "1.0",
                StringComparison.Ordinal)
            || response.DocumentId == Guid.Empty
            || response.DocumentId != request.DocumentId
            || response.ProcessingAttemptId == Guid.Empty
            || response.ProcessingAttemptId != request.ProcessingAttemptId
            || response.Document is null
            || response.Pages is null
            || response.Warnings is null
            || response.ProcessingMetadata is null)
        {
            throw new InvalidDataException();
        }

        var outcome = MapOutcome(response.Status);
        var classification = MapClassification(
            response.Document.Classification);

        if (!string.Equals(
                response.Document.FileName,
                request.FileName,
                StringComparison.Ordinal)
            || !string.Equals(
                response.Document.ContentType,
                "application/pdf",
                StringComparison.Ordinal)
            || response.Document.SizeBytes != request.SizeBytes
            || response.Document.PageCount < 1
            || response.Document.PageCount > options.MaximumPageCount
            || response.Pages.Count != response.Document.PageCount)
        {
            throw new InvalidDataException();
        }

        var pages = new ProcessedPageData[response.Pages.Count];
        var pagesWithExtractableText = 0;

        for (var index = 0; index < response.Pages.Count; index++)
        {
            var page = response.Pages[index];
            var expectedPageNumber = index + 1;

            if (page is null
                || page.PageNumber != expectedPageNumber
                || page.Text is null
                || page.CharacterCount < 0)
            {
                throw new InvalidDataException();
            }

            var characterCount = page.Text.EnumerateRunes().Count();
            var hasExtractableText =
                !string.IsNullOrWhiteSpace(page.Text);

            if (page.CharacterCount != characterCount
                || page.HasExtractableText != hasExtractableText)
            {
                throw new InvalidDataException();
            }

            if (page.HasExtractableText)
            {
                pagesWithExtractableText++;
            }

            pages[index] = new ProcessedPageData(
                page.PageNumber,
                page.Text,
                page.CharacterCount,
                page.HasExtractableText);
        }

        ValidateClassificationInvariants(
            outcome,
            classification,
            response.Document.RequiresOcr,
            pagesWithExtractableText,
            response.Document.PageCount);

        var warnings =
            new ProcessingWarningData[response.Warnings.Count];

        for (var index = 0; index < response.Warnings.Count; index++)
        {
            var warning = response.Warnings[index];

            if (warning is null
                || string.IsNullOrWhiteSpace(warning.Code)
                || string.IsNullOrWhiteSpace(warning.Message)
                || warning.PageNumbers is null)
            {
                throw new InvalidDataException();
            }

            var pageNumbers = warning.PageNumbers.ToArray();

            for (var pageIndex = 0;
                 pageIndex < pageNumbers.Length;
                 pageIndex++)
            {
                var pageNumber = pageNumbers[pageIndex];

                if (pageNumber < 1
                    || pageNumber > response.Document.PageCount
                    || (pageIndex > 0
                        && pageNumber <= pageNumbers[pageIndex - 1]))
                {
                    throw new InvalidDataException();
                }
            }

            warnings[index] = new ProcessingWarningData(
                warning.Code,
                warning.Message,
                pageNumbers);
        }

        ValidateWarnings(
            classification,
            pages,
            warnings);

        if (!string.Equals(
                response.ProcessingMetadata.Method,
                "pymupdf",
                StringComparison.Ordinal)
            || response.ProcessingMetadata.DurationMs < 0)
        {
            throw new InvalidDataException();
        }

        var canonicalPayloadJson = JsonSerializer.Serialize(
            response,
            SerializerOptions);

        return new DocumentProcessingResponseData(
            response.SchemaVersion!,
            response.DocumentId,
            response.ProcessingAttemptId,
            outcome,
            new ProcessedDocumentData(
                response.Document.FileName!,
                response.Document.ContentType!,
                response.Document.SizeBytes,
                response.Document.PageCount,
                classification,
                response.Document.RequiresOcr),
            pages,
            warnings,
            new ProcessingMetadataData(
                response.ProcessingMetadata.Method!,
                response.ProcessingMetadata.DurationMs),
            canonicalPayloadJson);
    }

    private static DocumentProcessingClientResult ParseErrorResponse(
        string payloadJson,
        int statusCode)
    {
        using var jsonDocument = JsonDocument.Parse(
            payloadJson,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

        ValidateExactObjectProperties(
            jsonDocument.RootElement,
            "schemaVersion",
            "errorCode",
            "message");

        var errorResponse = JsonSerializer.Deserialize<ErrorResponseDto>(
            payloadJson,
            SerializerOptions)
            ?? throw new InvalidDataException();

        if (errorResponse.SchemaVersion is not { } schemaVersion
            || errorResponse.ErrorCode is not { } errorCode
            || errorResponse.Message is not { } message)
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        if (!string.Equals(
                schemaVersion,
                "1.0",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(errorCode)
            || string.IsNullOrWhiteSpace(message)
            || !IsValidRemoteError(
                statusCode,
                errorCode,
                message))
        {
            return DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.InvalidResponse);
        }

        var remoteError = new DocumentProcessingRemoteError(
            statusCode,
            schemaVersion,
            errorCode,
            message);

        var failure = statusCode == 500
            ? DocumentProcessingClientFailure.ServiceError
            : DocumentProcessingClientFailure.RemoteRejection;

        return DocumentProcessingClientResult.RemoteFailure(
            failure,
            remoteError);
    }

    private static void ValidateSuccessJsonShape(
        JsonElement root)
    {
        ValidateExactObjectProperties(
            root,
            "schemaVersion",
            "documentId",
            "processingAttemptId",
            "status",
            "document",
            "pages",
            "warnings",
            "processingMetadata");

        ValidateExactObjectProperties(
            root.GetProperty("document"),
            "fileName",
            "contentType",
            "sizeBytes",
            "pageCount",
            "classification",
            "requiresOcr");

        var pages = root.GetProperty("pages");

        if (pages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException();
        }

        foreach (var page in pages.EnumerateArray())
        {
            ValidateExactObjectProperties(
                page,
                "pageNumber",
                "text",
                "characterCount",
                "hasExtractableText");
        }

        var warnings = root.GetProperty("warnings");

        if (warnings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException();
        }

        foreach (var warning in warnings.EnumerateArray())
        {
            ValidateExactObjectProperties(
                warning,
                "code",
                "message",
                "pageNumbers");
        }

        ValidateExactObjectProperties(
            root.GetProperty("processingMetadata"),
            "method",
            "durationMs");
    }

    private static void ValidateExactObjectProperties(
        JsonElement element,
        params string[] expectedPropertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var expectedProperties = new HashSet<string>(
            expectedPropertyNames,
            StringComparer.Ordinal);
        var observedProperties = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!observedProperties.Add(property.Name)
                || !expectedProperties.Contains(property.Name))
            {
                throw new InvalidDataException();
            }
        }

        if (!observedProperties.SetEquals(expectedProperties))
        {
            throw new InvalidDataException();
        }
    }

    private static DocumentProcessingOutcome MapOutcome(
        string? status)
    {
        return status switch
        {
            "COMPLETED" => DocumentProcessingOutcome.Completed,
            "REQUIRES_REVIEW" =>
                DocumentProcessingOutcome.RequiresReview,
            _ => throw new InvalidDataException()
        };
    }

    private static PdfClassification MapClassification(
        string? classification)
    {
        return classification switch
        {
            "PDF_TEXT" => PdfClassification.PdfText,
            "PDF_SCANNED" => PdfClassification.PdfScanned,
            "PDF_MIXED" => PdfClassification.PdfMixed,
            _ => throw new InvalidDataException()
        };
    }

    private static void ValidateClassificationInvariants(
        DocumentProcessingOutcome outcome,
        PdfClassification classification,
        bool requiresOcr,
        int pagesWithExtractableText,
        int pageCount)
    {
        DocumentProcessingOutcome expectedOutcome;
        PdfClassification expectedClassification;
        bool expectedRequiresOcr;

        if (pagesWithExtractableText == pageCount)
        {
            expectedOutcome = DocumentProcessingOutcome.Completed;
            expectedClassification = PdfClassification.PdfText;
            expectedRequiresOcr = false;
        }
        else if (pagesWithExtractableText == 0)
        {
            expectedOutcome = DocumentProcessingOutcome.RequiresReview;
            expectedClassification = PdfClassification.PdfScanned;
            expectedRequiresOcr = true;
        }
        else
        {
            expectedOutcome = DocumentProcessingOutcome.RequiresReview;
            expectedClassification = PdfClassification.PdfMixed;
            expectedRequiresOcr = true;
        }

        if (outcome != expectedOutcome
            || classification != expectedClassification
            || requiresOcr != expectedRequiresOcr)
        {
            throw new InvalidDataException();
        }
    }

    private static bool IsValidRemoteError(
        int statusCode,
        string errorCode,
        string message)
    {
        return statusCode switch
        {
            413 =>
                errorCode == "FILE_TOO_LARGE"
                && message ==
                "The uploaded file exceeds the maximum allowed size.",
            415 =>
                errorCode == "UNSUPPORTED_FILE_TYPE"
                && message ==
                "Only application/pdf files are supported.",
            422 => errorCode switch
            {
                "INVALID_REQUEST" =>
                    message == "The request is invalid.",
                "INVALID_CORRELATION_ID" =>
                    message ==
                    "A valid X-Correlation-ID header is required.",
                "EMPTY_FILE" =>
                    message == "The uploaded file is empty.",
                "INVALID_PDF" =>
                    message == "The uploaded file is not a valid PDF.",
                "PDF_PASSWORD_REQUIRED" =>
                    message == "The PDF requires a password.",
                "PDF_PAGE_LIMIT_EXCEEDED" =>
                    message ==
                    "The PDF exceeds the maximum allowed page count.",
                _ => false
            },
            500 =>
                errorCode == "INTERNAL_SERVER_ERROR"
                && message == "An unexpected error occurred.",
            _ => false
        };
    }

    private static void ValidateWarnings(
        PdfClassification classification,
        IReadOnlyList<ProcessedPageData> pages,
        IReadOnlyList<ProcessingWarningData> warnings)
    {
        switch (classification)
        {
            case PdfClassification.PdfText:
                if (warnings.Count != 0)
                {
                    throw new InvalidDataException();
                }

                break;

            case PdfClassification.PdfScanned:
                ValidateSingleWarning(
                    warnings,
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    Enumerable.Range(1, pages.Count));
                break;

            case PdfClassification.PdfMixed:
                ValidateSingleWarning(
                    warnings,
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    pages
                        .Where(page => !page.HasExtractableText)
                        .Select(page => page.PageNumber));
                break;

            default:
                throw new InvalidDataException();
        }
    }

    private static void ValidateSingleWarning(
        IReadOnlyList<ProcessingWarningData> warnings,
        string expectedCode,
        string expectedMessage,
        IEnumerable<int> expectedPageNumbers)
    {
        if (warnings.Count != 1)
        {
            throw new InvalidDataException();
        }

        var warning = warnings[0];

        if (!string.Equals(
                warning.Code,
                expectedCode,
                StringComparison.Ordinal)
            || !string.Equals(
                warning.Message,
                expectedMessage,
                StringComparison.Ordinal)
            || !warning.PageNumbers.SequenceEqual(expectedPageNumbers))
        {
            throw new InvalidDataException();
        }
    }

    private sealed class SuccessResponseDto
    {
        public string? SchemaVersion { get; init; }

        public Guid DocumentId { get; init; }

        public Guid ProcessingAttemptId { get; init; }

        public string? Status { get; init; }

        public DocumentDto? Document { get; init; }

        public List<PageDto?>? Pages { get; init; }

        public List<WarningDto?>? Warnings { get; init; }

        public ProcessingMetadataDto? ProcessingMetadata { get; init; }
    }

    private sealed class DocumentDto
    {
        public string? FileName { get; init; }

        public string? ContentType { get; init; }

        public long SizeBytes { get; init; }

        public int PageCount { get; init; }

        public string? Classification { get; init; }

        public bool RequiresOcr { get; init; }
    }

    private sealed class PageDto
    {
        public int PageNumber { get; init; }

        public string? Text { get; init; }

        public int CharacterCount { get; init; }

        public bool HasExtractableText { get; init; }
    }

    private sealed class WarningDto
    {
        public string? Code { get; init; }

        public string? Message { get; init; }

        public List<int>? PageNumbers { get; init; }
    }

    private sealed class ProcessingMetadataDto
    {
        public string? Method { get; init; }

        public int DurationMs { get; init; }
    }

    private sealed class ErrorResponseDto
    {
        public string? SchemaVersion { get; init; }

        public string? ErrorCode { get; init; }

        public string? Message { get; init; }
    }

    private sealed class NonDisposingStream(Stream innerStream) : Stream
    {
        public override bool CanRead => innerStream.CanRead;

        public override bool CanSeek => innerStream.CanSeek;

        public override bool CanWrite => innerStream.CanWrite;

        public override long Length => innerStream.Length;

        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush()
        {
            innerStream.Flush();
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            return innerStream.FlushAsync(cancellationToken);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            return innerStream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return innerStream.Read(buffer);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return innerStream.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            return innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            innerStream.SetLength(value);
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            innerStream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            innerStream.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return innerStream.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return innerStream.WriteAsync(buffer, cancellationToken);
        }

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            return innerStream.CopyToAsync(
                destination,
                bufferSize,
                cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
