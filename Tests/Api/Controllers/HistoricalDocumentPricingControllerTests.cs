using Api.Controllers;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Contracts.HistoricalPricing;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class HistoricalDocumentPricingControllerTests
{
    [Fact]
    public async Task Estimate_WithPdf_UsesCanonicalExtractionAndReturnsAggregate()
    {
        var context = Context(Response([Item(1, "PV-06")]), Aggregate());

        var action = await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken);

        var response = Response(action);
        Assert.Equal(1, response.SourceCount);
        Assert.Equal(1, response.ExtractedElementCount);
        Assert.Equal(330m, response.Expected);
        Assert.All(response.Items.Where(item => item.PricingStatus == "PRICEABLE"),
            item =>
            {
                Assert.Equal(item.LineExpected, item.Expected);
                Assert.Equal(item.UnitExpected, item.LineExpected);
            });
        await context.Pricing.Received(1).PriceAsync(
            Arg.Is<IReadOnlyList<StructuredItemData>>(items =>
                items != null && items.Count == 1),
            Arg.Any<IReadOnlyList<ProcessingWarningData>>(),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Estimate_WithMultipleFiles_SendsOneAi2RequestContainingAllFiles()
    {
        var context = Context(Response([]), EmptyAggregate());
        DocumentProcessingClientRequest? captured = null;
        context.Ai2.ProcessAsync(
                Arg.Do<DocumentProcessingClientRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(Response([]));

        var action = await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files =
                [
                    File("quote.pdf", "application/pdf"),
                    File("details.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                ],
                ProjectId = Guid.NewGuid(),
                RequirementId = Guid.NewGuid()
            },
            TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(action.Result);
        await context.Ai2.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal(2, captured.Files.Count);
    }

    [Fact]
    public async Task Estimate_WithPartialPricing_ReportsTwoPriceableAndOneNotPriceable()
    {
        var items = new[] { Item(1, "A"), Item(2, "B"), Item(3, "C") };
        var response = Response(await Context(Response(items), Aggregate()).Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(3, response.ItemCount);
        Assert.Equal(2, response.PricedItemCount);
        Assert.Equal(1, response.NotPriceableItemCount);
        Assert.True(response.IsPartial);
        Assert.Contains(response.Items, item => item.PricingStatus == "NOT_PRICEABLE");
    }

    [Fact]
    public async Task Estimate_WhenAi2Unavailable_ReturnsBadGateway()
    {
        var context = Context(
            DocumentProcessingClientResult.Failed(
                DocumentProcessingClientFailure.ServiceUnavailable),
            Aggregate());

        var action = await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        await context.Pricing.DidNotReceive().PriceAsync(
            Arg.Any<IReadOnlyList<StructuredItemData>>(),
            Arg.Any<IReadOnlyList<ProcessingWarningData>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Estimate_WhenCorpusUnavailable_ReturnsServiceUnavailableBeforeAi2()
    {
        var context = Context(Response([]), EmptyAggregate(), corpusAvailable: false);

        var action = await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        await context.Ai2.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Estimate_WhenAi2ReturnsNoItems_ReturnsEmptyControlledResponse()
    {
        var response = Response(await Context(Response([]), EmptyAggregate()).Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(0, response.ExtractedElementCount);
        Assert.Equal(0, response.ItemCount);
        Assert.Null(response.Minimum);
        Assert.Null(response.Expected);
        Assert.Null(response.Maximum);
    }

    [Fact]
    public async Task Estimate_PropagatesWarningsAndUsesPublicQuotedPricingBasis()
    {
        var warnings = new[]
        {
            new ProcessingWarningData(
                "MEASUREMENT_AREA_MISMATCH", "Area mismatch", [])
        };
        var aggregate = Aggregate() with
        {
            Warnings = ["MEASUREMENT_AREA_MISMATCH"]
        };
        var context = Context(Response([Item(1, "PV-06")], warnings), aggregate);

        var response = Response(await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.pdf", "application/pdf")]
            },
            TestContext.Current.CancellationToken));

        Assert.Equal("PUBLIC_QUOTED_ITEM_PRICES", response.PricingBasis);
        Assert.Contains("MEASUREMENT_AREA_MISMATCH", response.Warnings);
        Assert.Equal(330m, response.Expected);
        await context.Pricing.Received(1).PriceAsync(
            Arg.Any<IReadOnlyList<StructuredItemData>>(),
            Arg.Is<IReadOnlyList<ProcessingWarningData>>(values =>
                values != null
                && values.Count == 1
                && values[0].Code == "MEASUREMENT_AREA_MISMATCH"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Estimate_WithInvalidMimeExtensionCombination_ReturnsBadRequest()
    {
        var context = Context(Response([]), EmptyAggregate());

        var action = await context.Controller.Estimate(
            new HistoricalDocumentEstimateForm
            {
                Files = [File("quote.xlsx", "application/pdf")]
            },
            TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        await context.Ai2.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static TestContextData Context(
        DocumentProcessingClientResult ai2Result,
        PricedRequirementExtraction aggregate,
        bool corpusAvailable = true)
    {
        var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
        ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(ai2Result);
        var pricing = Substitute.For<IPriceRequirementExtractionService>();
        pricing.PriceAsync(
                Arg.Any<IReadOnlyList<StructuredItemData>>(),
                Arg.Any<IReadOnlyList<ProcessingWarningData>>(),
                Arg.Any<CancellationToken>())
            .Returns(aggregate);
        var corpus = Substitute.For<IHistoricalQuoteCorpus>();
        corpus.Current.Returns(corpusAvailable
            ? new HistoricalCorpusSnapshot(true, "path", DateTimeOffset.UtcNow, [], [])
            : HistoricalCorpusSnapshot.Unavailable(null));
        corpus.ReloadAsync(Arg.Any<CancellationToken>())
            .Returns(HistoricalCorpusSnapshot.Unavailable(null));
        return new TestContextData(
            new HistoricalDocumentPricingController(
                new HistoricalDocumentEstimatePipeline(ai2, pricing, corpus),
                NullLogger<HistoricalDocumentPricingController>.Instance),
            ai2,
            pricing);
    }

    private static DocumentProcessingClientResult Response(
        IReadOnlyList<StructuredItemData> items,
        IReadOnlyList<ProcessingWarningData>? warnings = null)
    {
        var documentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var extraction = new StructuredExtractionData(
            StructuredExtractionStatus.Completed,
            "Project",
            null,
            null,
            [],
            [],
            [],
            items,
            [],
            [],
            [],
            items.Count,
            0,
            0,
            items.Count,
            "rule_based_v2",
            10);
        return DocumentProcessingClientResult.Success(
            new DocumentProcessingResponseData(
                "AI2-1.0",
                documentId,
                attemptId,
                DocumentProcessingOutcome.Completed,
                new ProcessedDocumentData(
                    "quote.pdf",
                    "application/pdf",
                    10,
                    1,
                    DocumentClassification.PdfText,
                    false),
                [],
                warnings ?? [],
                new ProcessingMetadataData("ai2", 10),
                "{}",
                extraction,
                DocumentProcessingProvider.Ai2));
    }

    private static StructuredItemData Item(int sequence, string reference) =>
        new(
            sequence,
            reference,
            "Puerta",
            StructuredElementType.Door,
            "1000 x 1000",
            1000,
            1000,
            1,
            false,
            [],
            [],
            [],
            null,
            null,
            1m,
            "CORREDIZA",
            0.9m,
            CanonicalExtractionValueStatus.Explicit);

    private static PricedRequirementExtraction Aggregate()
    {
        var priceableA = AggregateItem(1, "A", 100m, 110m, 120m);
        var priceableB = AggregateItem(2, "B", 200m, 220m, 240m);
        var notPriceable = new PricedRequirementExtractionItem(
            3,
            "C",
            RequirementElementPricingStatus.NotPriceable,
            Query(),
            Technical(null, null, null),
            Commercial(null, null, null),
            [],
            true);
        return new PricedRequirementExtraction(
            3,
            2,
            1,
            1,
            300m,
            330m,
            360m,
            "COP",
            0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            true,
            true,
            [],
            ["NO_COMPARABLES"],
            [],
            [priceableA, priceableB, notPriceable]);
    }

    private static PricedRequirementExtraction EmptyAggregate() =>
        new(
            0, 0, 0, 0, null, null, null, null, 0m,
            HistoricalPriceConfidenceLevel.Low, false, false,
            [], [], [], []);

    private static PricedRequirementExtractionItem AggregateItem(
        int id,
        string reference,
        decimal minimum,
        decimal expected,
        decimal maximum) =>
        new(
            id,
            reference,
            RequirementElementPricingStatus.Priceable,
            Query(),
            Technical(minimum, expected, maximum),
            Commercial(minimum, expected, maximum),
            [],
            false);

    private static HistoricalCandidateQuery Query() =>
        new("PUERTA", "3831", "TEMPLADO", 6m, "CORREDIZA", 1000,
            1000, 1m, "NEGRO", 1m, 5);

    private static HistoricalTechnicalPriceEstimate Technical(
        decimal? minimum,
        decimal? expected,
        decimal? maximum) =>
        new(
            "COP", minimum, expected, maximum, 0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            "HISTORICAL_COMPARABLES", 5, 5, 1,
            expected is null, [], expected is null ? ["NO_COMPARABLES"] : [],
            [], []);

    private static HistoricalCommercialPriceEstimate Commercial(
        decimal? minimum,
        decimal? expected,
        decimal? maximum) =>
        new(
            "COP",
            "HISTORICAL_COMPARABLES",
            HistoricalPricingBasis.PublicQuotedItemPrices,
            minimum,
            expected,
            maximum,
            0m, 0m, 0m,
            0m, 0m, 0m,
            0m, 0m, 0m,
            0m, 0m, 0m,
            minimum,
            expected,
            maximum,
            0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            expected is null,
            [],
            expected is null ? ["NO_COMPARABLES"] : []);

    private static IFormFile File(string name, string contentType)
    {
        var stream = new MemoryStream([1, 2, 3]);
        return new FormFile(stream, 0, stream.Length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static HistoricalDocumentEstimateResponse Response(
        ActionResult<HistoricalDocumentEstimateResponse> action) =>
        Assert.IsType<HistoricalDocumentEstimateResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);

    private sealed record TestContextData(
        HistoricalDocumentPricingController Controller,
        IAi2DocumentProcessingClient Ai2,
        IPriceRequirementExtractionService Pricing);
}
