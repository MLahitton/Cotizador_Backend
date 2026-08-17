using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using CotizadorBackend.Tests.TestDoubles;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ProcessClaimedDocumentProcessingAttemptServiceTests
{
    private const string PdfContentType =
        "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly Guid AttemptId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt =
        CreatedAt.AddSeconds(10);

    [Fact]
    public async Task ProcessAsync_SendsPdfContentTypeToClient()
    {
        var context = new Context(contentType: PdfContentType);
        var requestStream = new MemoryStream([1, 2, 3]);
        context.Storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(requestStream));
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(DocumentProcessingOutcome.Completed));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        await context.Client.Received(1).ProcessAsync(
            Arg.Is<DocumentProcessingClientRequest>(request =>
                request.ContentType == PdfContentType
                && request.FileName == "document.pdf"
                && request.Content == requestStream),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_SendsXlsxContentTypeToClient()
    {
        var context = new Context(
            contentType: XlsxContentType,
            originalFileName: "document.xlsx",
            storageKey: "prequotes/document.xlsx");
        var requestStream = new MemoryStream([1, 2, 3, 4]);
        context.Storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(requestStream));
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(DocumentProcessingOutcome.Completed));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        await context.Client.Received(1).ProcessAsync(
            Arg.Is<DocumentProcessingClientRequest>(request =>
                request.ContentType == XlsxContentType
                && request.FileName == "document.xlsx"
                && request.Content == requestStream),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed, DocumentClassification.PdfText, false, "pymupdf", 1)]
    [InlineData(DocumentProcessingOutcome.Completed, DocumentClassification.PdfScanned, true, "pymupdf", 1)]
    [InlineData(DocumentProcessingOutcome.Completed, DocumentClassification.PdfMixed, true, "pymupdf", 1)]
    [InlineData(DocumentProcessingOutcome.Completed, DocumentClassification.Xlsx, false, "openpyxl", 0)]
    public async Task ProcessAsync_PreservesDocumentClassificationInExtractionResult(
        DocumentProcessingOutcome outcome,
        DocumentClassification classification,
        bool requiresOcr,
        string processingMethod,
        int pageCount)
    {
        var context = new Context();
        DocumentExtractionResult? extractionResult = null;
        context.Repository.When(value => value.AddResult(
                Arg.Any<DocumentExtractionResult>()))
            .Do(call => extractionResult = call.Arg<DocumentExtractionResult>());
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(
                outcome,
                classification,
                requiresOcr,
                processingMethod,
                pageCount));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        Assert.NotNull(extractionResult);
        Assert.Equal(classification, extractionResult!.Classification);
        Assert.Equal(requiresOcr, extractionResult!.RequiresOcr);
        Assert.Equal(pageCount, extractionResult!.PageCount);
        Assert.Equal(processingMethod, extractionResult!.ProcessingMethod);
    }
    [Theory]
    [InlineData(DocumentProcessingOutcome.Completed)]
    [InlineData(DocumentProcessingOutcome.RequiresReview)]
    public async Task ProcessAsync_WithSuccess_FinalizesAndCreatesResult(
        DocumentProcessingOutcome outcome)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(
                outcome,
                classification: DocumentClassification.PdfText,
                requiresOcr: false));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        Assert.Equal(
            DocumentProcessingState.Finished,
            context.Attempt.ProcessingState);
        Assert.Equal(outcome, context.Attempt.Outcome);
        Assert.Null(context.Attempt.ErrorCode);
        context.Repository.Received(1).AddResult(
            Arg.Any<DocumentExtractionResult>());
        context.Repository.Received(1).AddStructuredExtraction(
            Arg.Is<StructuredDocumentExtraction>(extraction =>
                extraction != null
                && extraction.ProjectName == "Synthetic project"
                && extraction.Items.Single().WidthMillimeters == 1200
                && extraction.Items.Single().HeightMillimeters == 1000
                && extraction.Items.Single().Quantity == 2
                && extraction.Requirements.Count == 1
                && extraction.DocumentReferences.Count == 1));
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResponseWithoutStructuredExtraction_FailsWithoutResults()
    {
        var context = new Context();
        var clientResult = CreateSuccess(
            DocumentProcessingOutcome.Completed);
        var response = Assert.IsType<DocumentProcessingResponseData>(
            clientResult.Response);
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Success(
                response with { StructuredExtraction = null }));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal(
            DocumentProcessingOutcome.Failed,
            context.Attempt.Outcome);
        Assert.Equal("AI_INVALID_RESPONSE", context.Attempt.ErrorCode);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        context.Repository.DidNotReceive().AddStructuredExtraction(
            Arg.Any<StructuredDocumentExtraction>());
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DocumentProcessingClientFailure.Timeout, "AI_SERVICE_TIMEOUT")]
    [InlineData(DocumentProcessingClientFailure.ServiceUnavailable, "AI_SERVICE_UNAVAILABLE")]
    [InlineData(DocumentProcessingClientFailure.InvalidResponse, "AI_INVALID_RESPONSE")]
    public async Task ProcessAsync_WithClientFailure_FinalizesWithoutResult(
        DocumentProcessingClientFailure failure,
        string errorCode)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Failed(failure));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal(DocumentProcessingOutcome.Failed, context.Attempt.Outcome);
        Assert.Equal(errorCode, context.Attempt.ErrorCode);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("INVALID_REQUEST")]
    [InlineData("INVALID_CORRELATION_ID")]
    [InlineData("EMPTY_FILE")]
    [InlineData("INVALID_PDF")]
    [InlineData("PDF_PASSWORD_REQUIRED")]
    [InlineData("PDF_PAGE_LIMIT_EXCEEDED")]
    [InlineData("FILE_TOO_LARGE")]
    [InlineData("UNSUPPORTED_FILE_TYPE")]
    public async Task ProcessAsync_WithRemoteRejection_PersistsExactCode(
        string errorCode)
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.RemoteFailure(
                DocumentProcessingClientFailure.RemoteRejection,
                new DocumentProcessingRemoteError(
                    422,
                    "1.0",
                    errorCode,
                    "Remote message.")));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal(errorCode, context.Attempt.ErrorCode);
        context.Repository.DidNotReceive().AddResult(
            Arg.Any<DocumentExtractionResult>());
    }

    [Theory]
    [InlineData("invalid_key")]
    [InlineData("read_error")]
    public async Task ProcessAsync_WithStorageFailure_FinalizesFailed(
        string scenario)
    {
        var context = new Context();
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(scenario == "invalid_key"
                ? Task.FromException<Stream>(new InvalidStorageKeyException())
                : Task.FromException<Stream>(
                    new FileStorageReadException(new IOException())));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        Assert.Equal("DOCUMENT_STORAGE_ERROR", context.Attempt.ErrorCode);
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("finished")]
    public async Task ProcessAsync_WithInvalidState_DoesNoExternalWork(
        string state)
    {
        var context = new Context(started: false);

        if (state == "finished")
        {
            context.Attempt.Start(CreatedAt.AddSeconds(1));
            context.Attempt.Fail("AI_SERVICE_TIMEOUT", CreatedAt.AddSeconds(2));
        }

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.InvalidState,
            result);
        await context.Storage.DidNotReceive().OpenReadAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await context.Client.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithMissingAttempt_ReturnsNotFound()
    {
        var context = new Context();
        context.Repository.FindProcessingWorkItemAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns((DocumentProcessingWorkItem?)null);

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.NotFound,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WithQueryError_ReturnsQueryError()
    {
        var context = new Context();
        context.Repository.FindProcessingWorkItemAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DocumentProcessingWorkItem?>(
                new DocumentProcessingQueryException(
                    new InvalidOperationException())));

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.QueryError,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WithTerminalPersistenceError_DoesNotReportSuccess()
    {
        var context = new Context();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccess(DocumentProcessingOutcome.Completed));
        context.Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new DocumentProcessingPersistenceException(
                    new InvalidOperationException())));

        var result = await context.Service.ProcessAsync(
            AttemptId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ProcessClaimedDocumentProcessingAttemptResult.PersistenceError,
            result);
    }

    [Fact]
    public async Task ProcessAsync_WhenHostCancels_LeavesProcessingAndRethrows()
    {
        var context = new Context();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        context.Storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<Stream>(cancellationSource.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Service.ProcessAsync(
                AttemptId,
                cancellationSource.Token));

        Assert.Equal(
            DocumentProcessingState.Processing,
            context.Attempt.ProcessingState);
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithV3Glass_ResolvesFourCodesAndPersistsAtomically()
    {
        var context = new Context();
        var codes = new[]
        {
            "LAM_4_4",
            "LAM_4_4_GRAY",
            "LAM_5_5",
            "LAM_5_5_GRAY"
        };
        var glassTypeIds = codes.ToDictionary(
            code => code,
            _ => Guid.NewGuid(),
            StringComparer.Ordinal);
        var rangeIds = codes.ToDictionary(
            code => code, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var prices = new (decimal Minimum, decimal Expected, decimal Maximum)[]
        {
            (90000m, 100000m, 110000m), (95000m, 95000m, 95000m),
            (120000m, 130000m, 140000m), (125000m, 135000m, 145000m)
        };
        context.GlassCatalog.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns(codes.Select((code, index) => new GlassTypeCatalogReadModel(
                glassTypeIds[code], code, code, null, true,
                new GlassPriceRangeCatalogReadModel(
                    rangeIds[code], 1, prices[index].Minimum,
                    prices[index].Expected,
                    prices[index].Maximum, "COP",
                    global::Domain.Catalogs.GlassPriceRangeStatus.Preliminary,
                    CreatedAt, null))).ToArray());
        var baseline = CreateSuccess(DocumentProcessingOutcome.Completed)
            .Response!;
        var structured = baseline.StructuredExtraction!;
        var items = codes.Select((code, index) =>
            structured.Items[0] with
            {
                Sequence = index + 1,
                Reference = $"W-{index + 1:00}",
                Description = $"Window {index + 1}",
                WidthMillimeters = new[] { 1500, 2100, 6200, 3800 }[index],
                HeightMillimeters = new[] { 1000, 1400, 3300, 1100 }[index],
                Quantity = new[] { 3, 4, 1, 2 }[index],
                Glass = new StructuredItemGlassData(
                    code,
                    code,
                    GlassAssignmentScope.Item,
                    false,
                    [],
                    [1],
                    [new SourceEvidenceData(
                        1,
                        EvidenceSourceType.Native,
                        $"Vidrio {code}")])
            }).ToArray();
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Success(baseline with
            {
                SchemaVersion = "3.0",
                StructuredExtraction = structured with
                {
                    Items = items,
                    ItemCount = 4,
                    KnownQuoteableUnitCount = 10,
                    IdentifiedGlassItemCount = 4,
                    GlassItemsRequiringReview = 0
                }
            }));
        StructuredDocumentExtraction? persisted = null;
        context.Repository.When(value => value.AddStructuredExtraction(
                Arg.Any<StructuredDocumentExtraction>()))
            .Do(call => persisted = call.Arg<StructuredDocumentExtraction>());

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        Assert.NotNull(persisted);
        Assert.Equal(DocumentProcessingState.Finished,
            context.Attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Completed,
            context.Attempt.Outcome);
        Assert.Null(context.Attempt.ErrorCode);
        Assert.NotNull(context.Attempt.CompletedAtUtc);
        Assert.Equal(4, persisted.Items.Count);
        Assert.All(persisted.Items, item =>
        {
            var glass = Assert.IsType<
                StructuredExtractionItemGlassDetection>(
                    item.GlassDetection);
            var code = Assert.IsType<string>(glass.NormalizedCodeSnapshot);
            Assert.Equal(glassTypeIds[code], glass.GlassTypeId);
            var valuation = Assert.IsType<
                StructuredExtractionItemGlassValuation>(item.GlassValuation);
            Assert.Equal(rangeIds[code], valuation.GlassPriceRangeVersionId);
            Assert.Equal(1, valuation.PriceRangeVersion);
            Assert.Equal(global::Domain.Catalogs.GlassPriceRangeStatus.Preliminary,
                valuation.PriceRangeStatus);
            Assert.Equal("COP", valuation.Currency);
            Assert.Equal(TimeSpan.Zero, valuation.CalculatedAtUtc.Offset);
        });
        Assert.Equal(
            new decimal?[] { 1.5m, 2.94m, 20.46m, 4.18m },
            persisted.Items.Select(item =>
                item.GlassValuation!.UnitAreaSquareMeters));
        Assert.Equal(
            new decimal?[] { 4.5m, 11.76m, 20.46m, 8.36m },
            persisted.Items.Select(item =>
                item.GlassValuation!.TotalAreaSquareMeters));
        Assert.Equal(
            new decimal?[] { 405000m, 1117200m, 2455200m, 1045000m },
            persisted.Items.Select(item =>
                item.GlassValuation!.MinimumAmount));
        Assert.Equal(
            new decimal?[] { 495000m, 1117200m, 2864400m, 1212200m },
            persisted.Items.Select(item =>
                item.GlassValuation!.MaximumAmount));
        context.Repository.Received(1).AddResult(
            Arg.Any<DocumentExtractionResult>());
        context.Repository.Received(1).AddStructuredExtraction(persisted);
        await context.GlassCatalog.Received(1)
            .GetActiveWithCurrentPriceRangesAsync(
                TestContext.Current.CancellationToken);
        await context.Repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_WithAssignedUnnormalizedGlass_PersistsWithoutGlassType()
    {
        var context = new Context();
        context.GlassCatalog.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns([]);
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateV3Success(new StructuredItemGlassData(
                "Vidrio laminado de seguridad 12 mm",
                null,
                GlassAssignmentScope.Item,
                true,
                [GlassReviewReason.GlassTypeNotIdentified],
                [1],
                [new SourceEvidenceData(
                    1, EvidenceSourceType.Native,
                    "Vidrio laminado de seguridad 12 mm")] )));
        StructuredDocumentExtraction? persisted = null;
        context.Repository.When(value => value.AddStructuredExtraction(
                Arg.Any<StructuredDocumentExtraction>()))
            .Do(call => persisted = call.Arg<StructuredDocumentExtraction>());

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessClaimedDocumentProcessingAttemptResult.Completed,
            result);
        var glass = Assert.Single(persisted!.Items).GlassDetection!;
        Assert.Null(glass.GlassTypeId);
        Assert.Equal(GlassAssignmentScope.Item, glass.AssignmentScope);
        Assert.Equal("Vidrio laminado de seguridad 12 mm",
            glass.RawSpecification);
        Assert.Single(glass.ReviewReasons);
        Assert.Single(glass.Evidence);
    }

    [Fact]
    public async Task ProcessAsync_WithUnknownNormalizedCode_ReportsCatalogCategory()
    {
        var context = new Context();
        context.GlassCatalog.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns([]);
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateV3Success(new StructuredItemGlassData(
                "Unknown glass", "UNKNOWN", GlassAssignmentScope.Item,
                false, [], [1],
                [new SourceEvidenceData(
                    1, EvidenceSourceType.Native, "Unknown glass")] )));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        context.Diagnostics.Received(1).CatalogResolutionFailed(
            DocumentId, AttemptId, context.Attempt.CorrelationId,
            "unknown_code",
            "UNKNOWN",
            1,
            Arg.Is<IReadOnlyList<string>>(codes =>
                codes != null && codes.Count == 0));
        context.Repository.DidNotReceive().AddStructuredExtraction(
            Arg.Any<StructuredDocumentExtraction>());
    }

    [Fact]
    public async Task ProcessAsync_WithCatalogQueryError_ReportsTechnicalCategory()
    {
        var context = new Context();
        context.GlassCatalog.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<GlassTypeCatalogReadModel>>(
                new GlassTypeCatalogQueryException(
                    new InvalidOperationException())));
        context.Client.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateV3Success(new StructuredItemGlassData(
                "Laminado 4+4", "LAM_4_4", GlassAssignmentScope.Item,
                false, [], [1],
                [new SourceEvidenceData(
                    1, EvidenceSourceType.Native, "Laminado 4+4")] )));

        var result = await context.Service.ProcessAsync(
            context.Attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessClaimedDocumentProcessingAttemptResult.Failed,
            result);
        context.Diagnostics.Received(1).CatalogResolutionFailed(
            DocumentId, AttemptId, context.Attempt.CorrelationId,
            "query_error", null);
    }

    private static DocumentProcessingClientResult CreateV3Success(
        StructuredItemGlassData glass)
    {
        var baseline = CreateSuccess(DocumentProcessingOutcome.Completed)
            .Response!;
        var structured = baseline.StructuredExtraction!;
        var item = structured.Items[0] with
        {
            RequiresReview = glass.RequiresReview,
            ReviewReasons = glass.RequiresReview
                ? [StructuredIssueCode.GlassTypeNotIdentified]
                : [],
            Glass = glass
        };
        return DocumentProcessingClientResult.Success(baseline with
        {
            SchemaVersion = "3.0",
            Outcome = glass.RequiresReview
                ? DocumentProcessingOutcome.RequiresReview
                : DocumentProcessingOutcome.Completed,
            StructuredExtraction = structured with
            {
                Status = glass.RequiresReview
                    ? StructuredExtractionStatus.RequiresReview
                    : StructuredExtractionStatus.Completed,
                Items = [item],
                ItemsRequiringReview = glass.RequiresReview ? 1 : 0,
                IdentifiedGlassItemCount =
                    glass.NormalizedCode is null ? 0 : 1,
                GlassItemsRequiringReview = glass.RequiresReview ? 1 : 0,
                ProcessingMethod = "rule_based_v2"
            }
        });
    }

    private static DocumentProcessingClientResult CreateSuccess(
        DocumentProcessingOutcome outcome,
        DocumentClassification classification = DocumentClassification.PdfText,
        bool? requiresOcr = null,
        string processingMethod = "pymupdf",
        int pageCount = 1)
    {
        var isXlsx = classification == DocumentClassification.Xlsx;
        var resolvedRequiresOcr = requiresOcr
            ?? outcome == DocumentProcessingOutcome.RequiresReview
                || outcome == DocumentProcessingOutcome.Completed
                    && classification
                        is DocumentClassification.PdfScanned
                        or DocumentClassification.PdfMixed;
        var classificationValue = classification switch
        {
            DocumentClassification.PdfText => "PDF_TEXT",
            DocumentClassification.PdfScanned => "PDF_SCANNED",
            DocumentClassification.PdfMixed => "PDF_MIXED",
            DocumentClassification.Xlsx => "XLSX",
            _ => throw new InvalidOperationException()
        };
        var payloadStatus = outcome == DocumentProcessingOutcome.Completed
            ? "COMPLETED"
            : "REQUIRES_REVIEW";
        var schemaVersion = "2.0";
        var payload = DocumentProcessingPayloadFactory.CreateSuccess(
            DocumentId,
            AttemptId,
            classification: classificationValue,
            pageCount: pageCount,
            status: payloadStatus,
            requiresOcr: resolvedRequiresOcr,
            fileName: isXlsx
                ? "document.xlsx"
                : "document.pdf",
            contentType: isXlsx
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/pdf",
            sizeBytes: 100,
            method: processingMethod,
            schemaVersion: schemaVersion,
            durationMs: 10,
            structuredMethod: schemaVersion is "3.0"
                ? processingMethod
                : "rule_based_v1");
        return DocumentProcessingClientResult.Success(
            new DocumentProcessingResponseData(
                "2.0",
                DocumentId,
                AttemptId,
                outcome,
                new ProcessedDocumentData(
                    isXlsx
                        ? "document.xlsx"
                        : "document.pdf",
                    isXlsx
                        ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                        : "application/pdf",
                    100,
                    pageCount,
                    classification,
                    resolvedRequiresOcr),
                [new ProcessedPageData(1,
                    resolvedRequiresOcr ? "" : "Text",
                    resolvedRequiresOcr ? 0 : 4,
                    !resolvedRequiresOcr)],
                [],
                new ProcessingMetadataData(processingMethod, 10),
                payload,
                CreateStructuredExtraction(outcome)));
    }

    private static StructuredExtractionData CreateStructuredExtraction(
        DocumentProcessingOutcome outcome)
    {
        var requiresReview =
            outcome == DocumentProcessingOutcome.RequiresReview;
        return new StructuredExtractionData(
            requiresReview
                ? StructuredExtractionStatus.RequiresReview
                : StructuredExtractionStatus.Completed,
            "Synthetic project",
            "Synthetic client",
            "Bogota",
            [1],
            [new SourceEvidenceData(
                1,
                requiresReview
                    ? EvidenceSourceType.Ocr
                    : EvidenceSourceType.Native,
                "Evidence")],
            [new StructuredRequirementData(
                RequirementCategory.GlassSpecification,
                "Tempered glass",
                [])],
            [new StructuredItemData(
                1,
                "W-01",
                "Window",
                StructuredElementType.Window,
                "1200 x 1000 mm",
                1200,
                1000,
                2,
                requiresReview,
                requiresReview
                    ? [StructuredIssueCode.OcrReviewRequired]
                    : [],
                [1],
                [])],
            [new StructuredDocumentReferenceData(
                1,
                "PLAN-01",
                "Drawing",
                null,
                99,
                [1],
                [])],
            requiresReview
                ? [new StructuredIssueData(
                    1,
                    StructuredIssueCode.OcrReviewRequired,
                    "Review OCR.",
                    1,
                    [1])]
                : [],
            [],
            1,
            1,
            requiresReview ? 1 : 0,
            2,
            "rule_based_v1",
            5);
    }

    private sealed class Context
    {
        public Context(
            bool started = true,
            string contentType = PdfContentType,
            string originalFileName = "document.pdf",
            string storageKey = "prequotes/document.pdf")
        {
            Repository = Substitute.For<IDocumentProcessingRepository>();
            Storage = Substitute.For<IFileStorage>();
            Client = Substitute.For<IDocumentProcessingClient>();
            GlassCatalog = Substitute.For<IGlassTypeCatalogRepository>();
            ProductSystems = Substitute.For<IProductSystemCatalogRepository>();
            Frames = Substitute.For<IFrameTypeCatalogRepository>();
            Finishes = Substitute.For<IFinishTypeCatalogRepository>();
            Aliases = Substitute.For<ICatalogAliasRepository>();
            Diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
            Attempt = DocumentProcessingAttempt.Create(
                DocumentId,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatedAt);
            if (started)
            {
                Attempt.Start(CreatedAt.AddSeconds(1));
            }

            Source = new DocumentProcessingSource(
                DocumentId,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                originalFileName,
                contentType,
                100,
                storageKey,
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                true,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                true);
            Repository.FindProcessingWorkItemAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => new DocumentProcessingWorkItem(Attempt, Source));
            Repository.ListDocumentSourcesByPreQuoteIdAsync(
                    Source.PreQuoteId,
                    Arg.Any<CancellationToken>())
                .Returns(_ => new[] { Source });
            Repository.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Storage.OpenReadAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Stream>(
                    new MemoryStream([1, 2, 3])));
            Client.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(DocumentProcessingClientResult.Failed(
                    DocumentProcessingClientFailure.Timeout));
            ProductSystems.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            Frames.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            Finishes.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            Aliases.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns([]);
            Service = new ProcessClaimedDocumentProcessingAttemptService(
                Repository,
                Storage,
                Client,
                GlassCatalog,
                ProductSystems,
                Frames,
                Finishes,
                Aliases,
                new FixedTimeProvider(CompletedAt),
                Diagnostics);
        }

        public IDocumentProcessingRepository Repository { get; }
        public IFileStorage Storage { get; }
        public IDocumentProcessingClient Client { get; }
        public IGlassTypeCatalogRepository GlassCatalog { get; }
        public IProductSystemCatalogRepository ProductSystems { get; }
        public IFrameTypeCatalogRepository Frames { get; }
        public IFinishTypeCatalogRepository Finishes { get; }
        public ICatalogAliasRepository Aliases { get; }
        public IDocumentProcessingDiagnostics Diagnostics { get; }
        public DocumentProcessingAttempt Attempt { get; }
        public DocumentProcessingSource Source { get; }
        public ProcessClaimedDocumentProcessingAttemptService Service { get; }
    }
}
