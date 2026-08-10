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
using NSubstitute;
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
    private const string PdfContentType = "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string OpenxmlMetadataMethod = "openpyxl";
    private const string PdfMetadataMethod = "pymupdf";

    [Fact]
    public async Task ProcessAsync_SendsPdfMultipartRequest()
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
            "/api/v3/prequotes/document-extractions",
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
        Assert.Equal(PdfContentType, filePart.ContentType);
        Assert.Equal([1, 2, 3, 4], filePart.Bytes);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ProcessAsync_SendsXlsxMultipartRequest()
    {
        var payload = CreateXlsxSuccessPayload();
        var source = new MemoryStream([9, 8, 7, 6]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            source: source,
            sourceFileName: "document.xlsx",
            sourceContentType: XlsxContentType);

        Assert.True(execution.Result.IsSuccess);
        Assert.Equal(
            "multipart/form-data",
            execution.Request.ContentType);
        var filePart = Assert.Single(
            execution.Request.Parts,
            part => part.Name == "file");
        Assert.Equal("document.xlsx", filePart.FileName);
        Assert.Equal(XlsxContentType, filePart.ContentType);
        Assert.Equal([9, 8, 7, 6], filePart.Bytes);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task ProcessAsync_WithValidXlsxResponse_ReturnsSuccess()
    {
        var payload = CreateXlsxSuccessPayload();

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            sourceFileName: "document.xlsx",
            sourceContentType: XlsxContentType);

        Assert.True(execution.Result.IsSuccess);
        Assert.NotNull(execution.Result.Response);
        Assert.Equal(DocumentClassification.Xlsx,
            execution.Result.Response.Document.Classification);
        Assert.Equal(XlsxContentType, execution.Result.Response.Document.ContentType);
        Assert.Equal(0, execution.Result.Response.Document.PageCount);
        Assert.Empty(execution.Result.Response.Warnings);
        Assert.Equal("openpyxl", execution.Result.Response.ProcessingMetadata.Method);
        Assert.NotNull(execution.Result.Response.StructuredExtraction);

        var projectEvidence = Assert.Single(
            execution.Result.Response.StructuredExtraction.ProjectEvidence);
        Assert.Equal(EvidenceSourceType.Xlsx, projectEvidence.SourceType);
        Assert.Equal("Cotizacion", projectEvidence.SheetName);
        Assert.Equal("A12:H12", projectEvidence.CellRange);

        var item = Assert.Single(execution.Result.Response.StructuredExtraction.Items);
        var itemEvidence = Assert.Single(item.Evidence);
        Assert.Equal(EvidenceSourceType.Xlsx, itemEvidence.SourceType);
        Assert.Equal("Cotizacion", itemEvidence.SheetName);
        Assert.Equal("A12:H12", itemEvidence.CellRange);
        Assert.NotNull(item.Glass);
        var itemGlass = item.Glass!;
        Assert.Equal(global::Domain.PreQuotes.GlassAssignmentScope.Unassigned,
            itemGlass.AssignmentScope);
        Assert.True(itemGlass.RequiresReview);
        Assert.Equal(
            [GlassReviewReason.GlassTypeNotIdentified],
            itemGlass.ReviewReasons);
        Assert.Empty(itemGlass.Evidence);
    }

    [Fact]
    public async Task ProcessAsync_WithAssignedXlsxGlassAndSourcePagesEmpty_ReturnsSuccess()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
              {
                  var item = root["structuredExtraction"]!["items"]![0]!.AsObject();
                  item["sourcePages"] = new JsonArray();
                  item["requiresReview"] = false;
                  item["reviewReasons"] = new JsonArray();
                  root["structuredExtraction"]!["status"] = "COMPLETED";
                  var glass = item["glass"]!.AsObject();
                  glass["rawSpecification"] = "Vidrio templado 8 mm";
                  glass["normalizedCode"] = "TEMP_8";
                  glass["assignmentScope"] = "ITEM";
                  glass["requiresReview"] = false;
                glass["reviewReasons"] = new JsonArray();
                glass["sourcePages"] = new JsonArray();
                glass["evidence"] = CreateXlsxEvidenceArray();

                var summary = root["structuredExtraction"]!["summary"]!.AsObject();
                summary["itemsRequiringReview"] = 0;
                summary["identifiedGlassItemCount"] = 1;
                summary["glassItemsRequiringReview"] = 0;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            sourceFileName: "document.xlsx",
            sourceContentType: XlsxContentType);

        Assert.True(execution.Result.IsSuccess);
        Assert.NotNull(execution.Result.Response);
        Assert.NotNull(execution.Result.Response.StructuredExtraction);

        var item = Assert.Single(execution.Result.Response.StructuredExtraction.Items);
        Assert.NotNull(item.Glass);
        var itemGlass = item.Glass!;
        Assert.Equal(
            global::Domain.PreQuotes.GlassAssignmentScope.Item,
            itemGlass.AssignmentScope);
        Assert.False(itemGlass.RequiresReview);
        Assert.Equal("TEMP_8", itemGlass.NormalizedCode);
        Assert.Equal("Vidrio templado 8 mm", itemGlass.RawSpecification);
        Assert.Empty(itemGlass.SourcePages);
        Assert.Single(itemGlass.Evidence);
    }

    [Fact]
    public async Task ProcessAsync_WithAssignedPdfGlassAndMatchingSourcePages_ReturnsSuccess()
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification: "PDF_TEXT",
                pageCount: 1,
                status: "COMPLETED"),
            root =>
            {
                var item = root["structuredExtraction"]!["items"]![0]!.AsObject();
                item["sourcePages"] = new JsonArray(JsonValue.Create(1));

                var itemEvidence = CreatePdfEvidenceArray(1);
                item["evidence"] = itemEvidence;

                var glass = item["glass"]!.AsObject();
                glass["rawSpecification"] = "Vidrio templado 8 mm";
                glass["normalizedCode"] = "TEMP_8";
                glass["assignmentScope"] = "ITEM";
                glass["requiresReview"] = false;
                glass["reviewReasons"] = new JsonArray();
                glass["sourcePages"] = new JsonArray(JsonValue.Create(1));
                glass["evidence"] = CreatePdfEvidenceArray(1);
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
        Assert.NotNull(execution.Result.Response);
        Assert.NotNull(execution.Result.Response.StructuredExtraction);

        var item = Assert.Single(execution.Result.Response.StructuredExtraction.Items);
        Assert.NotNull(item.Glass);
        var itemGlass = item.Glass!;
        Assert.Equal(
            global::Domain.PreQuotes.GlassAssignmentScope.Item,
            itemGlass.AssignmentScope);
        Assert.False(itemGlass.RequiresReview);
        Assert.Equal("TEMP_8", itemGlass.NormalizedCode);
        Assert.Equal("Vidrio templado 8 mm", itemGlass.RawSpecification);
        Assert.Equal(1, itemGlass.SourcePages.Single());
        Assert.Single(itemGlass.Evidence);
    }

    [Fact]
    public async Task ProcessAsync_WithAssignedPdfGlassAndInconsistentSourcePages_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification: "PDF_TEXT",
                pageCount: 1,
                status: "COMPLETED"),
            root =>
            {
                var item = root["structuredExtraction"]!["items"]![0]!.AsObject();
                item["sourcePages"] = new JsonArray(JsonValue.Create(1));
                item["evidence"] = CreatePdfEvidenceArray(1);

                var glass = item["glass"]!.AsObject();
                glass["rawSpecification"] = "Vidrio templado 8 mm";
                glass["normalizedCode"] = "TEMP_8";
                glass["assignmentScope"] = "ITEM";
                glass["requiresReview"] = false;
                glass["reviewReasons"] = new JsonArray();
                glass["sourcePages"] = new JsonArray(JsonValue.Create(1));
                glass["evidence"] = CreatePdfEvidenceArray(2);
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithXlsxAndPdfContentType_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
            {
                root["document"]!["contentType"] = PdfContentType;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithXlsxAndPageCountOne_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
            {
                root["document"]!["pageCount"] = 1;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithXlsxAndPlaceholderPage_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
            {
                root["document"]!["pageCount"] = 0;
                root["pages"] = new JsonArray(new JsonObject
                {
                    ["pageNumber"] = 1,
                    ["text"] = "placeholder",
                    ["characterCount"] = 11,
                    ["hasExtractableText"] = true
                });
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithXlsxAndRequiresOcr_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
            {
                root["document"]!["requiresOcr"] = true;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithXlsxAndPdfMetadataMethod_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            CreateXlsxSuccessPayload(),
            root =>
            {
                root["processingMetadata"]!["method"] = PdfMetadataMethod;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithPdfAndXlsxMetadataMethod_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification: "PDF_TEXT",
                pageCount: 1,
                status: "COMPLETED"),
            root =>
            {
                root["processingMetadata"]!["method"] = OpenxmlMetadataMethod;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithPdfAndZeroPageCount_ReturnsInvalidResponse()
    {
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification: "PDF_TEXT",
                pageCount: 1,
                status: "COMPLETED"),
            root =>
            {
                root["document"]!["pageCount"] = 0;
                root["pages"] = new JsonArray();
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithUnsupportedContentType_Rejected()
    {
        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId);

        var execution = await ExecuteAsyncWithoutRequestCapture(
            () => CreateJsonResponse(200, payload),
            sourceContentType: "application/unknown",
            diagnostics: diagnostics);

        Assert.Equal(
            DocumentProcessingClientFailure.InvalidResponse,
            execution.Result.Failure);
        Assert.Null(execution.Result.Response);
        Assert.Null(execution.Result.RemoteError);
        Assert.Null(execution.Request);
        diagnostics.Received(1).ContractRejected(
            documentId: DocumentId,
            processingAttemptId: AttemptId,
            correlationId: CorrelationId,
            httpStatusCode: null,
            stage: "content_type",
            category: "unsupported_content_type",
            itemSequence: null,
            rejectedNormalizedCode: null,
            acceptedNormalizedCodes: Arg.Is<IReadOnlyList<string>?>(values => values == null),
            exceptionType: "ContractValidationException",
            exceptionMessage:
                "Contract validation failed at content_type: unsupported_content_type.",
            rejectedValue: "application/unknown",
            acceptedValues: Arg.Is<IReadOnlyList<string>>(values =>
                values != null
                && values.Count == 2
                && values.Contains(PdfContentType)
                && values.Contains(XlsxContentType)));
    }

    [Fact]
    public async Task ProcessAsync_WithMissingRootProperty_ReportsMissingProperties()
    {
        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                "PDF_TEXT",
                1),
            root =>
            {
                root.Remove("status");
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            diagnostics: diagnostics);

        AssertInvalidResponse(execution.Result);
        diagnostics.Received(1).ContractRejected(
            documentId: DocumentId,
            processingAttemptId: AttemptId,
            correlationId: CorrelationId,
            httpStatusCode: 200,
            stage: "root_shape",
            category: "invalid_shape",
            itemSequence: Arg.Any<int?>(),
            rejectedNormalizedCode: Arg.Any<string?>(),
            acceptedNormalizedCodes: Arg.Any<IReadOnlyList<string>?>(),
            exceptionType: "ContractValidationException",
            exceptionMessage: Arg.Is<string>(message =>
                message.Contains("Path=$")
                && message.Contains("Missing=[status]")
                && message.Contains("Expected=")
                && message.Contains("Actual=")),
            jsonPath: Arg.Any<string>(),
            fieldName: Arg.Any<string?>(),
            rejectedValue: Arg.Any<string?>(),
            lineNumber: Arg.Any<long?>(),
            bytePositionInLine: Arg.Any<long?>(),
            acceptedValues: Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task ProcessAsync_WithExtraRootProperty_ReportsExtraProperties()
    {
        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                "PDF_TEXT",
                1),
            root =>
            {
                root["extraRootProperty"] = true;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            diagnostics: diagnostics);

        AssertInvalidResponse(execution.Result);
        diagnostics.Received(1).ContractRejected(
            documentId: DocumentId,
            processingAttemptId: AttemptId,
            correlationId: CorrelationId,
            httpStatusCode: 200,
            stage: "root_shape",
            category: "invalid_shape",
            itemSequence: Arg.Any<int?>(),
            rejectedNormalizedCode: Arg.Any<string?>(),
            acceptedNormalizedCodes: Arg.Any<IReadOnlyList<string>?>(),
            exceptionType: "ContractValidationException",
            exceptionMessage: Arg.Is<string>(message =>
                message.Contains("Path=$")
                && message.Contains("Extra=[extraRootProperty]")
                && message.Contains("Expected=")),
            jsonPath: Arg.Any<string>(),
            fieldName: Arg.Any<string?>(),
            rejectedValue: Arg.Any<string?>(),
            lineNumber: Arg.Any<long?>(),
            bytePositionInLine: Arg.Any<long?>(),
            acceptedValues: Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidPagesType_ReportsExpectedAndActualJsonKind()
    {
        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var payload = MutateSuccessPayload(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                "PDF_TEXT",
                1),
            root =>
            {
                root["pages"] = 10;
            });

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            diagnostics: diagnostics);

        AssertInvalidResponse(execution.Result);
        diagnostics.Received(1).ContractRejected(
            documentId: DocumentId,
            processingAttemptId: AttemptId,
            correlationId: CorrelationId,
            httpStatusCode: 200,
            stage: "root_shape",
            category: "invalid_shape",
            itemSequence: Arg.Any<int?>(),
            rejectedNormalizedCode: Arg.Any<string?>(),
            acceptedNormalizedCodes: Arg.Any<IReadOnlyList<string>?>(),
            exceptionType: "ContractValidationException",
            exceptionMessage: Arg.Is<string>(message =>
                message.Contains("Path=$")
                && message.Contains("Property=$.pages")
                && message.Contains("ExpectedKind=Array")
                && message.Contains("ActualKind=Number")),
            jsonPath: Arg.Any<string>(),
            fieldName: Arg.Any<string?>(),
            rejectedValue: Arg.Any<string?>(),
            lineNumber: Arg.Any<long?>(),
            bytePositionInLine: Arg.Any<long?>(),
            acceptedValues: Arg.Any<IReadOnlyList<string>?>());
    }

    [Theory]
    [InlineData(
        "PDF_TEXT",
        1,
        DocumentProcessingOutcome.Completed,
        DocumentClassification.PdfText,
        false)]
    [InlineData(
        "PDF_SCANNED",
        2,
        DocumentProcessingOutcome.RequiresReview,
        DocumentClassification.PdfScanned,
        true)]
    [InlineData(
        "PDF_MIXED",
        2,
        DocumentProcessingOutcome.RequiresReview,
        DocumentClassification.PdfMixed,
        true)]
    [InlineData(
        "XLSX",
        0,
        DocumentProcessingOutcome.RequiresReview,
        DocumentClassification.Xlsx,
        false)]
    public async Task ProcessAsync_WithValidSuccess_ReturnsMappedResponse(
        string externalClassification,
        int pageCount,
        DocumentProcessingOutcome expectedOutcome,
        DocumentClassification expectedClassification,
        bool expectedRequiresOcr)
    {
        var payload = externalClassification == "XLSX"
            ? CreateXlsxSuccessPayload()
            : DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                externalClassification,
                pageCount);
        var fileName = externalClassification == "XLSX"
            ? "document.xlsx"
            : "document.pdf";
        var contentType = externalClassification == "XLSX"
            ? XlsxContentType
            : PdfContentType;

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload),
            sourceFileName: fileName,
            sourceContentType: contentType);

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

    [Fact]
    public async Task ProcessAsync_WithMixedPdfAndSuccessfulOcr_ReturnsSuccess()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "Native page", 11, true),
                new PayloadPage(2, "OCR page", 8, true)
            ],
            warnings:
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    [2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
    }

    [Fact]
    public async Task ProcessAsync_WithMixedPdfAndEmptyOcrPage_ReturnsSuccess()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "Native page", 11, true),
                new PayloadPage(2, string.Empty, 0, false)
            ],
            warnings:
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    [2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
    }

    [Fact]
    public async Task ProcessAsync_WithScannedPdfAndSuccessfulOcr_ReturnsSuccess()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_SCANNED",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "OCR page 1", 10, true),
                new PayloadPage(2, "OCR page 2", 10, true)
            ],
            warnings:
            [
                new PayloadWarning(
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    [1, 2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
    }

    [Fact]
    public async Task ProcessAsync_WithScannedPdfAndOneEmptyOcrPage_ReturnsSuccess()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_SCANNED",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "OCR page", 8, true),
                new PayloadPage(2, string.Empty, 0, false)
            ],
            warnings:
            [
                new PayloadWarning(
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    [1, 2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
    }

    [Fact]
    public async Task ProcessAsync_WithTextPdfAndExtractableText_ReturnsSuccess()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_TEXT",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "Native page 1", 13, true),
                new PayloadPage(2, "Native page 2", 13, true)
            ],
            warnings: []);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        Assert.True(execution.Result.IsSuccess);
    }

    [Fact]
    public async Task ProcessAsync_WithTextPdfAndEmptyPage_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_TEXT",
            pageCount: 2,
            pages:
            [
                new PayloadPage(1, "Native page", 11, true),
                new PayloadPage(2, string.Empty, 0, false)
            ],
            warnings: []);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithMixedPdfAndNoWarning_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            warnings: []);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithMixedPdfAndEmptyWarning_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            warnings:
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    [])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithMixedPdfWarningForAllPages_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 2,
            warnings:
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    [1, 2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithMixedPdfAndEmptyUnwarnedPage_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_MIXED",
            pageCount: 3,
            pages:
            [
                new PayloadPage(1, string.Empty, 0, false),
                new PayloadPage(2, "OCR page", 8, true),
                new PayloadPage(3, "Native page", 11, true)
            ],
            warnings:
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    [2])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
    }

    [Fact]
    public async Task ProcessAsync_WithScannedPdfWarningMissingPage_ReturnsInvalidResponse()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "PDF_SCANNED",
            pageCount: 2,
            warnings:
            [
                new PayloadWarning(
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    [1])
            ]);

        var execution = await ExecuteAsync(
            () => CreateJsonResponse(200, payload));

        AssertInvalidResponse(execution.Result);
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
                        root["schemaVersion"] = "1.0";
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
                "processingMetadata",
                "structuredExtraction"
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
        MemoryStream? source = null,
        string sourceContentType = PdfContentType,
        string sourceFileName = "document.pdf",
        IDocumentProcessingDiagnostics? diagnostics = null)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000/")
        };
        var client = new CotizadorAiDocumentProcessingClient(
            httpClient,
            options ?? CreateOptions(),
            diagnostics);
        var content = source ?? new MemoryStream([1, 2, 3, 4]);

        var result = await client.ProcessAsync(
            new DocumentProcessingClientRequest(
                DocumentId,
                AttemptId,
                CorrelationId,
                sourceFileName,
                sourceContentType,
                content.Length,
                content),
            CancellationToken.None);

        return new ClientExecution(
            result,
            Assert.IsType<CapturedHttpRequest>(handler.LastRequest),
            content);
    }

    private static async Task<ClientExecutionWithoutRequestCapture> ExecuteAsyncWithoutRequestCapture(
        Func<HttpResponseMessage> responseFactory,
        CotizadorAiOptions? options = null,
        MemoryStream? source = null,
        string sourceContentType = PdfContentType,
        string sourceFileName = "document.pdf",
        IDocumentProcessingDiagnostics? diagnostics = null)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000/")
        };
        var client = new CotizadorAiDocumentProcessingClient(
            httpClient,
            options ?? CreateOptions(),
            diagnostics);
        var content = source ?? new MemoryStream([1, 2, 3, 4]);

        var result = await client.ProcessAsync(
            new DocumentProcessingClientRequest(
                DocumentId,
                AttemptId,
                CorrelationId,
                sourceFileName,
                sourceContentType,
                content.Length,
                content),
            CancellationToken.None);

        Assert.Null(handler.LastRequest);
        return new ClientExecutionWithoutRequestCapture(
            result,
            handler.LastRequest,
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

    private static string CreateXlsxSuccessPayload()
    {
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: "XLSX",
            pageCount: 0,
            status: "REQUIRES_REVIEW",
            requiresOcr: false,
            warnings: [],
            method: OpenxmlMetadataMethod,
            contentType: XlsxContentType,
            fileName: "document.xlsx");
        return NormalizeXlsxStructuredContent(
            JsonNode.Parse(payload)!.AsObject()).ToJsonString();
    }

    private static JsonObject NormalizeXlsxStructuredContent(
        JsonObject root)
    {
        root["pages"] = new JsonArray();
        root["document"]!["contentType"] = XlsxContentType;
        root["document"]!["classification"] = "XLSX";
        var structured = root["structuredExtraction"]!.AsObject();

        var project = structured["project"]!.AsObject();
        project["sourcePages"] = new JsonArray();
        project["evidence"] = CreateXlsxEvidenceArray();

        var requirements = structured["requirements"]!.AsObject();
        foreach (var item in requirements["glassSpecifications"]!.AsArray())
        {
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        foreach (var item in requirements["profileSpecifications"]!.AsArray())
        {
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        foreach (var item in requirements["finishes"]!.AsArray())
        {
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        foreach (var item in requirements["accessoriesAndSealants"]!.AsArray())
        {
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        foreach (var item in requirements["generalNotes"]!.AsArray())
        {
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        foreach (var item in structured["items"]!.AsArray())
        {
            var itemObject = item.AsObject();
            itemObject["sourcePages"] = new JsonArray();
            itemObject["evidence"] = CreateXlsxEvidenceArray();

            var glass = itemObject["glass"]!.AsObject();
            glass["rawSpecification"] = null;
            glass["normalizedCode"] = null;
            glass["assignmentScope"] = "UNASSIGNED";
            glass["requiresReview"] = true;
            glass["reviewReasons"] = new JsonArray(JsonValue.Create("GLASS_TYPE_NOT_IDENTIFIED"));
            glass["sourcePages"] = new JsonArray();
            glass["evidence"] = new JsonArray();
        }

        foreach (var item in structured["documentReferences"]!.AsArray())
        {
            item.AsObject()["sourcePages"] = new JsonArray();
            item.AsObject()["evidence"] = CreateXlsxEvidenceArray();
        }

        structured["issues"] = new JsonArray();
        structured["conflicts"] = new JsonArray();

        var summary = structured["summary"]!.AsObject();
        structured["project"]!["sourcePages"] = new JsonArray();
        summary["itemsRequiringReview"] = 1;
        summary["identifiedGlassItemCount"] = 0;
        summary["glassItemsRequiringReview"] = 1;

        return root;
    }

    private static JsonArray CreateXlsxEvidenceArray()
    {
        var evidence = new JsonArray();
        evidence.Add(new JsonObject
        {
            ["sourceType"] = "XLSX",
            ["text"] = "Hoja 1",
            ["sheetName"] = "Cotizacion",
            ["cellRange"] = "A12:H12"
        });
        return evidence;
    }

    private static JsonArray CreatePdfEvidenceArray(int pageNumber)
    {
        return new JsonArray(new JsonObject
        {
            ["sourceType"] = "NATIVE",
            ["pageNumber"] = pageNumber,
            ["text"] = "Hoja 1"
        });
    }

    private sealed record ClientExecution(
        DocumentProcessingClientResult Result,
        CapturedHttpRequest Request,
        MemoryStream Source);

    private sealed record ClientExecutionWithoutRequestCapture(
        DocumentProcessingClientResult Result,
        CapturedHttpRequest? Request,
        MemoryStream Source);
}
