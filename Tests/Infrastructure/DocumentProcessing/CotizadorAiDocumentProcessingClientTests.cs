using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.DocumentProcessing;
using CotizadorBackend.Tests.TestDoubles;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAiDocumentProcessingClientTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AttemptId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid CorrelationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ProcessAsync_SendsExactMultipartRequest()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId);
        var source = new MemoryStream([1, 2, 3, 4]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            source: source);

        Assert.True(execution.Result.IsSuccess);
        Assert.Equal(HttpMethod.Post, execution.Request.Method);
        Assert.Equal(
            "/api/v1/prequotes/document-extractions",
            execution.Request.RequestUri?.AbsolutePath);
        Assert.Equal(["application/json"], execution.Request.Accept);
        Assert.Equal(
            [CorrelationId.ToString("D")],
            execution.Request.CorrelationValues);
        Assert.Equal(
            "multipart/form-data",
            execution.Request.ContentType);
        Assert.Equal(3, execution.Request.Parts.Count);

        var documentIdPart = Assert.Single(
            execution.Request.Parts,
            part => part.Name == "documentId");
        var attemptIdPart = Assert.Single(
            execution.Request.Parts,
            part => part.Name == "processingAttemptId");
        var filePart = Assert.Single(
            execution.Request.Parts,
            part => part.Name == "file");

        Assert.Equal(DocumentId.ToString("D"), documentIdPart.Text);
        Assert.Equal(AttemptId.ToString("D"), attemptIdPart.Text);
        Assert.Equal("document.pdf", filePart.FileName);
        Assert.Equal("application/pdf", filePart.ContentType);
        Assert.Equal([1, 2, 3, 4], filePart.Bytes);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(
        "PDF_TEXT",
        1,
        DocumentProcessingOutcome.Completed,
        PdfClassification.PdfText,
        false)]
    [InlineData(
        "PDF_SCANNED",
        2,
        DocumentProcessingOutcome.RequiresReview,
        PdfClassification.PdfScanned,
        true)]
    [InlineData(
        "PDF_MIXED",
        2,
        DocumentProcessingOutcome.RequiresReview,
        PdfClassification.PdfMixed,
        true)]
    public async Task ProcessAsync_WithValidSuccess_ReturnsMappedResponse(
        string externalClassification,
        int pageCount,
        DocumentProcessingOutcome expectedOutcome,
        PdfClassification expectedClassification,
        bool expectedRequiresOcr)
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            externalClassification,
            pageCount);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
        Assert.NotNull(execution.Result.Response);
        Assert.Equal(expectedOutcome, execution.Result.Response.Outcome);
        Assert.Equal(
            expectedClassification,
            execution.Result.Response.Document.Classification);
        Assert.Equal(
            expectedRequiresOcr,
            execution.Result.Response.Document.RequiresOcr);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task ProcessAsync_EnforcesMaximumPageCount(
        int pageCount,
        bool expectedSuccess)
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            "PDF_TEXT",
            pageCount);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.Equal(expectedSuccess, execution.Result.IsSuccess);

        if (!expectedSuccess)
        {
            Assert.Equal(
                DocumentProcessingClientFailure.InvalidResponse,
                execution.Result.Failure);
        }
    }

    [Fact]
    public async Task ProcessAsync_WithDuplicateSuccessProperty_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId);
        payload = payload.Replace(
            "\"status\":\"COMPLETED\"",
            "\"status\":\"COMPLETED\",\"status\":\"REQUIRES_REVIEW\"",
            StringComparison.Ordinal);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("pages")]
    [InlineData("warnings")]
    [InlineData("page_count")]
    [InlineData("extractable")]
    public async Task ProcessAsync_WithWrongJsonType_ReturnsInvalidResponse(
        string scenario)
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId),
            root =>
            {
                switch (scenario)
                {
                    case "pages":
                        root["pages"] = new JsonObject();
                        break;
                    case "warnings":
                        root["warnings"] = new JsonObject();
                        break;
                    case "page_count":
                        root["document"]!["pageCount"] = "1";
                        break;
                    case "extractable":
                        root["pages"]![0]!["hasExtractableText"] = "true";
                        break;
                }
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithUnknownBodyStatus_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            status: "UNKNOWN");

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("trailing_comma")]
    public async Task ProcessAsync_WithInvalidSuccessJsonSyntax_ReturnsInvalidResponse(
        string scenario)
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId);
        payload = scenario == "comment"
            ? payload.Replace("{", "{/*comment*/", StringComparison.Ordinal)
            : payload[..^1] + ",}";

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("document_id")]
    [InlineData("attempt_id")]
    [InlineData("schema")]
    [InlineData("content_type")]
    [InlineData("classification")]
    [InlineData("method")]
    [InlineData("duration")]
    [InlineData("page_number")]
    [InlineData("character_count")]
    [InlineData("extractable")]
    [InlineData("unknown_property")]
    [InlineData("missing_property")]
    [InlineData("wrong_casing")]
    public async Task ProcessAsync_WithInvalidSuccessSemantics_ReturnsInvalidResponse(
        string scenario)
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                "PDF_MIXED",
                2),
            root =>
            {
                var document = root["document"]!.AsObject();
                var pages = root["pages"]!.AsArray();
                var metadata = root["processingMetadata"]!.AsObject();

                switch (scenario)
                {
                    case "document_id":
                        root["documentId"] =
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
                        break;
                    case "attempt_id":
                        root["processingAttemptId"] =
                            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
                        break;
                    case "schema":
                        root["schemaVersion"] = "2.0";
                        break;
                    case "content_type":
                        document["contentType"] = "text/plain";
                        break;
                    case "classification":
                        document["classification"] = "UNKNOWN";
                        break;
                    case "method":
                        metadata["method"] = "other";
                        break;
                    case "duration":
                        metadata["durationMs"] = -1;
                        break;
                    case "page_number":
                        pages[0]!["pageNumber"] = 2;
                        break;
                    case "character_count":
                        pages[0]!["characterCount"] = 999;
                        break;
                    case "extractable":
                        pages[0]!["hasExtractableText"] = false;
                        break;
                    case "unknown_property":
                        root["extra"] = true;
                        break;
                    case "missing_property":
                        root.Remove("status");
                        break;
                    case "wrong_casing":
                        var value = root["schemaVersion"]!.DeepClone();
                        root.Remove("schemaVersion");
                        root["SchemaVersion"] = value;
                        break;
                }
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidContentType_ReturnsInvalidResponse()
    {
        var response = CreateJsonResponse(
            200,
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId));
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("text/plain");

        var execution = await ExecuteAsync(() => response);

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithEmptyBody_ReturnsInvalidResponse()
    {
        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, string.Empty));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithOversizedBody_ReturnsInvalidResponse()
    {
        var options = CreateOptions(maximumResponseBytes: 10);
        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, new string('x', 11)),
            options);

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidUtf8_ReturnsInvalidResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xff, 0xfe])
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        AddCorrelation(response, CorrelationId.ToString("D"));

        var execution = await ExecuteAsync(() => response);

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithUnknownStatus_ReturnsInvalidResponse()
    {
        var execution = await ExecuteAsync(
            () => CreateJsonResponse(
                201,
                DocumentProcessingPayloadFactory.CreateSuccess(
                    DocumentId,
                    AttemptId)));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("pdf_text_warning")]
    [InlineData("scanned_missing")]
    [InlineData("scanned_additional")]
    [InlineData("scanned_code")]
    [InlineData("scanned_message")]
    [InlineData("scanned_missing_page")]
    [InlineData("scanned_additional_page")]
    [InlineData("scanned_duplicate")]
    [InlineData("scanned_disordered")]
    [InlineData("scanned_legacy_code")]
    [InlineData("mixed_missing")]
    [InlineData("mixed_additional")]
    [InlineData("mixed_code")]
    [InlineData("mixed_message")]
    [InlineData("mixed_text_page")]
    [InlineData("mixed_missing_no_text")]
    public async Task ProcessAsync_WithInvalidWarnings_ReturnsInvalidResponse(
        string scenario)
    {
        var classification = scenario.StartsWith(
            "scanned",
            StringComparison.Ordinal)
            ? "PDF_SCANNED"
            : scenario.StartsWith("mixed", StringComparison.Ordinal)
                ? "PDF_MIXED"
                : "PDF_TEXT";
        var pageCount = classification == "PDF_TEXT" ? 1 : 3;
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification,
                pageCount),
            root => MutateWarnings(root, scenario));

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData(
        422,
        "INVALID_REQUEST",
        "The request is invalid.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        422,
        "INVALID_CORRELATION_ID",
        "A valid X-Correlation-ID header is required.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        422,
        "EMPTY_FILE",
        "The uploaded file is empty.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        422,
        "INVALID_PDF",
        "The uploaded file is not a valid PDF.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        422,
        "PDF_PASSWORD_REQUIRED",
        "The PDF requires a password.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        422,
        "PDF_PAGE_LIMIT_EXCEEDED",
        "The PDF exceeds the maximum allowed page count.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        413,
        "FILE_TOO_LARGE",
        "The uploaded file exceeds the maximum allowed size.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        415,
        "UNSUPPORTED_FILE_TYPE",
        "Only application/pdf files are supported.",
        DocumentProcessingClientFailure.RemoteRejection)]
    [InlineData(
        500,
        "INTERNAL_SERVER_ERROR",
        "An unexpected error occurred.",
        DocumentProcessingClientFailure.ServiceError)]
    public async Task ProcessAsync_WithValidRemoteError_MapsFailure(
        int statusCode,
        string errorCode,
        string message,
        DocumentProcessingClientFailure expectedFailure)
    {
        var payload = DocumentProcessingPayloadFactory.CreateError(
            errorCode,
            message);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(statusCode, payload));

        Assert.Equal(expectedFailure, execution.Result.Failure);
        Assert.NotNull(execution.Result.RemoteError);
        Assert.Equal(errorCode, execution.Result.RemoteError.ErrorCode);
    }

    [Theory]
    [InlineData(422, "INVALID_REQUEST", "The request is invalid.")]
    [InlineData(
        422,
        "INVALID_CORRELATION_ID",
        "A valid X-Correlation-ID header is required.")]
    [InlineData(422, "EMPTY_FILE", "The uploaded file is empty.")]
    [InlineData(
        422,
        "INVALID_PDF",
        "The uploaded file is not a valid PDF.")]
    [InlineData(
        422,
        "PDF_PASSWORD_REQUIRED",
        "The PDF requires a password.")]
    [InlineData(
        422,
        "PDF_PAGE_LIMIT_EXCEEDED",
        "The PDF exceeds the maximum allowed page count.")]
    [InlineData(
        413,
        "FILE_TOO_LARGE",
        "The uploaded file exceeds the maximum allowed size.")]
    [InlineData(
        415,
        "UNSUPPORTED_FILE_TYPE",
        "Only application/pdf files are supported.")]
    [InlineData(
        500,
        "INTERNAL_SERVER_ERROR",
        "An unexpected error occurred.")]
    public async Task ProcessAsync_WithWrongRemoteMessage_ReturnsInvalidResponse(
        int statusCode,
        string errorCode,
        string validMessage)
    {
        var payload = DocumentProcessingPayloadFactory.CreateError(
            errorCode,
            $"{validMessage} changed");

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(statusCode, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("unknown_code")]
    [InlineData("wrong_status")]
    [InlineData("wrong_schema")]
    [InlineData("additional_property")]
    [InlineData("duplicate_property")]
    [InlineData("comment")]
    [InlineData("trailing_comma")]
    public async Task ProcessAsync_WithMalformedRemoteError_ReturnsInvalidResponse(
        string scenario)
    {
        var statusCode = 422;
        var payload = DocumentProcessingPayloadFactory.CreateError(
            "INVALID_PDF",
            "The uploaded file is not a valid PDF.");

        switch (scenario)
        {
            case "unknown_code":
                payload = DocumentProcessingPayloadFactory.CreateError(
                    "UNKNOWN",
                    "Unknown.");
                break;
            case "wrong_status":
                statusCode = 413;
                break;
            case "wrong_schema":
                payload = DocumentProcessingPayloadFactory.CreateError(
                    "INVALID_PDF",
                    "The uploaded file is not a valid PDF.",
                    "2.0");
                break;
            case "additional_property":
                payload = payload.Replace(
                    "}",
                    ",\"extra\":true}",
                    StringComparison.Ordinal);
                break;
            case "duplicate_property":
                payload = payload.Replace(
                    "\"schemaVersion\":\"1.0\"",
                    "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\"",
                    StringComparison.Ordinal);
                break;
            case "comment":
                payload = payload.Replace(
                    "{",
                    "{/*comment*/",
                    StringComparison.Ordinal);
                break;
            case "trailing_comma":
                payload = payload[..^1] + ",}";
                break;
        }

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(statusCode, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("empty")]
    [InlineData("different")]
    public async Task ProcessAsync_WithInvalidCorrelation_ReturnsInvalidResponse(
        string scenario)
    {
        var values = scenario switch
        {
            "missing" => Array.Empty<string>(),
            "duplicate" =>
            [
                CorrelationId.ToString("D"),
                CorrelationId.ToString("D")
            ],
            "invalid" => ["not-a-guid"],
            "empty" => [Guid.Empty.ToString("D")],
            _ => ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]
        };
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload, values));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData(413, "missing")]
    [InlineData(415, "duplicate")]
    [InlineData(422, "invalid")]
    [InlineData(500, "empty")]
    [InlineData(422, "different")]
    public async Task ProcessAsync_WithInvalidCorrelationOnRemoteError_ReturnsInvalidResponse(
        int statusCode,
        string correlationScenario)
    {
        var (errorCode, message) = statusCode switch
        {
            413 => (
                "FILE_TOO_LARGE",
                "The uploaded file exceeds the maximum allowed size."),
            415 => (
                "UNSUPPORTED_FILE_TYPE",
                "Only application/pdf files are supported."),
            422 => (
                "INVALID_CORRELATION_ID",
                "A valid X-Correlation-ID header is required."),
            500 => (
                "INTERNAL_SERVER_ERROR",
                "An unexpected error occurred."),
            _ => throw new InvalidOperationException()
        };
        var payload = DocumentProcessingPayloadFactory.CreateError(
            errorCode,
            message);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(
                statusCode,
                payload,
                CreateInvalidCorrelationValues(correlationScenario)));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData(408, "missing")]
    [InlineData(502, "different")]
    [InlineData(503, "missing")]
    [InlineData(504, "different")]
    public async Task ProcessAsync_WithInvalidCorrelationOnInfrastructureStatus_ReturnsInvalidResponse(
        int statusCode,
        string correlationScenario)
    {
        var execution = await ExecuteAsync(
            () => CreateJsonResponse(
                statusCode,
                "{}",
                CreateInvalidCorrelationValues(correlationScenario)));

        AssertInvalidResponse(execution.Result);
    }

    [Theory]
    [InlineData(503, DocumentProcessingClientFailure.ServiceUnavailable)]
    [InlineData(502, DocumentProcessingClientFailure.ServiceUnavailable)]
    [InlineData(504, DocumentProcessingClientFailure.Timeout)]
    [InlineData(408, DocumentProcessingClientFailure.Timeout)]
    public async Task ProcessAsync_WithInfrastructureStatusAndCorrelation_MapsFailure(
        int statusCode,
        DocumentProcessingClientFailure expectedFailure)
    {
        var execution = await ExecuteAsync(
            () => CreateJsonResponse(statusCode, "{}"));

        Assert.Equal(expectedFailure, execution.Result.Failure);
    }

    [Fact]
    public async Task ProcessAsync_ReserializesCanonicalSnapshotAndPreservesUnicode()
    {
        const string text = "Línea uno\nEmoji 😀; compuesto é; combinante e\u0301";
        var pages = new[]
        {
            new PayloadPage(
                1,
                text,
                text.EnumerateRunes().Count(),
                true),
            new PayloadPage(
                2,
                string.Empty,
                0,
                false)
        };
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            pages: pages,
            writeIndented: true);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
        var snapshot = execution.Result.Response!.PayloadJson;
        Assert.NotEqual(payload, snapshot);
        Assert.DoesNotContain(Environment.NewLine, snapshot);

        using var document = JsonDocument.Parse(snapshot);
        Assert.Equal(
            [
                "schemaVersion",
                "documentId",
                "processingAttemptId",
                "status",
                "document",
                "pages",
                "warnings",
                "processingMetadata"
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            [
                "fileName",
                "contentType",
                "sizeBytes",
                "pageCount",
                "classification",
                "requiresOcr"
            ],
            document.RootElement
                .GetProperty("document")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            [
                "pageNumber",
                "text",
                "characterCount",
                "hasExtractableText"
            ],
            document.RootElement
                .GetProperty("pages")[0]
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            [
                "code",
                "message",
                "pageNumbers"
            ],
            document.RootElement
                .GetProperty("warnings")[0]
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            [
                "method",
                "durationMs"
            ],
            document.RootElement
                .GetProperty("processingMetadata")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            text,
            document.RootElement
                .GetProperty("pages")[0]
                .GetProperty("text")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "correlationId",
                out _));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("1", true)]
    [InlineData("100", true)]
    public void FromConfiguration_ValidatesMaximumPageCount(
        string? configuredValue,
        bool expectedValid)
    {
        var values = new Dictionary<string, string?>
        {
            ["CotizadorAi:BaseUrl"] = "http://localhost:8000",
            ["CotizadorAi:TimeoutSeconds"] = "30",
            ["CotizadorAi:MaximumResponseBytes"] = "33554432",
            ["CotizadorAi:MaximumPageCount"] = configuredValue
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        if (expectedValid)
        {
            var options = CotizadorAiOptions.FromConfiguration(configuration);
            Assert.Equal(int.Parse(configuredValue!), options.MaximumPageCount);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(
                () => CotizadorAiOptions.FromConfiguration(configuration));
        }
    }

    private static async Task<ClientExecution> ExecuteAsync(
        Func<HttpResponseMessage> responseFactory,
        CotizadorAiOptions? options = null,
        MemoryStream? source = null)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000/")
        };
        var client = new CotizadorAiDocumentProcessingClient(
            httpClient,
            options ?? CreateOptions());
        var content = source ?? new MemoryStream([1, 2, 3, 4]);

        var result = await client.ProcessAsync(
            new DocumentProcessingClientRequest(
                DocumentId,
                AttemptId,
                CorrelationId,
                "document.pdf",
                4,
                content),
            CancellationToken.None);

        return new ClientExecution(
            result,
            Assert.IsType<CapturedHttpRequest>(handler.LastRequest),
            content);
    }

    private static CotizadorAiOptions CreateOptions(
        long maximumResponseBytes = 33_554_432,
        int maximumPageCount = 100)
    {
        return new CotizadorAiOptions(
            new Uri("http://localhost:8000/"),
            30,
            maximumResponseBytes,
            maximumPageCount);
    }

    private static HttpResponseMessage CreateJsonResponse(
        int statusCode,
        string payload,
        IReadOnlyList<string>? correlationValues = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json")
        };

        if (correlationValues is null)
        {
            AddCorrelation(response, CorrelationId.ToString("D"));
        }
        else
        {
            AddCorrelation(response, correlationValues.ToArray());
        }

        return response;
    }

    private static void AddCorrelation(
        HttpResponseMessage response,
        params string[] values)
    {
        response.Headers.TryAddWithoutValidation(
            "X-Correlation-ID",
            values);
    }

    private static IReadOnlyList<string> CreateInvalidCorrelationValues(
        string scenario)
    {
        return scenario switch
        {
            "missing" => Array.Empty<string>(),
            "duplicate" =>
            [
                CorrelationId.ToString("D"),
                CorrelationId.ToString("D")
            ],
            "invalid" => ["not-a-guid"],
            "empty" => [Guid.Empty.ToString("D")],
            "different" => ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"],
            _ => throw new InvalidOperationException()
        };
    }

    private static string MutateSuccessPayload(
        string payload,
        Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static void MutateWarnings(
        JsonObject root,
        string scenario)
    {
        var warnings = root["warnings"]!.AsArray();

        if (scenario == "pdf_text_warning")
        {
            warnings.Add(
                JsonSerializer.SerializeToNode(
                    new PayloadWarning(
                        "OCR_REQUIRED",
                        "The document does not contain extractable text.",
                        [1])));
            return;
        }

        if (scenario.EndsWith("missing", StringComparison.Ordinal))
        {
            warnings.Clear();
            return;
        }

        var warning = warnings[0]!.AsObject();
        var pageNumbers = warning["pageNumbers"]!.AsArray();

        switch (scenario)
        {
            case "scanned_additional":
            case "mixed_additional":
                warnings.Add(warning.DeepClone());
                break;
            case "scanned_code":
            case "mixed_code":
                warning["code"] = "UNKNOWN";
                break;
            case "scanned_message":
            case "mixed_message":
                warning["message"] = "Wrong message.";
                break;
            case "scanned_missing_page":
                pageNumbers.RemoveAt(pageNumbers.Count - 1);
                break;
            case "scanned_additional_page":
                pageNumbers.Add(4);
                break;
            case "scanned_duplicate":
                pageNumbers.Add(3);
                break;
            case "scanned_disordered":
                pageNumbers.Clear();
                pageNumbers.Add(2);
                pageNumbers.Add(1);
                pageNumbers.Add(3);
                break;
            case "scanned_legacy_code":
                warning["code"] = "NO_EXTRACTABLE_TEXT";
                break;
            case "mixed_text_page":
                pageNumbers.Add(1);
                break;
            case "mixed_missing_no_text":
                pageNumbers.Clear();
                break;
        }
    }

    private static void AssertInvalidResponse(
        DocumentProcessingClientResult result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            DocumentProcessingClientFailure.InvalidResponse,
            result.Failure);
        Assert.Null(result.Response);
        Assert.Null(result.RemoteError);
    }

    private sealed record ClientExecution(
        DocumentProcessingClientResult Result,
        CapturedHttpRequest Request,
        MemoryStream Source);
}
