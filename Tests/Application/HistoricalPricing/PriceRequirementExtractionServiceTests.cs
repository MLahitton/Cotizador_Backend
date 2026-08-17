using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class PriceRequirementExtractionServiceTests
{
    [Fact]
    public async Task PriceAsync_WithAcceptanceFixture_ReturnsPartialAggregate()
    {
        var service = Service(
            Priceable(1, "PV-06", 100m, 110m, 120m, 0.80m),
            Priceable(2, "V-14", 200m, 220m, 240m, 0.70m),
            NotPriceable(3, "X-01"));

        var result = await service.PriceAsync(
            [Item(1, "PV-06", 1), Item(2, "V-14", 1), Item(3, "X-01", 1)],
            [],
            TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ItemCount);
        Assert.Equal(2, result.PricedItemCount);
        Assert.Equal(1, result.NotPriceableItemCount);
        Assert.Equal(300m, result.CommercialMinimum);
        Assert.Equal(330m, result.CommercialExpected);
        Assert.Equal(360m, result.CommercialMaximum);
        Assert.True(result.IsPartial);
        Assert.True(result.RequiresReview);
        Assert.Equal("COP", result.Currency);
    }

    [Fact]
    public async Task PriceAsync_WithMultiplePriceableItems_SumsEachFinalRangeOnce()
    {
        var service = Service(
            Priceable(1, "A", 10m, 20m, 30m, 0.90m),
            Priceable(2, "B", 40m, 50m, 60m, 0.80m));

        var result = await service.PriceAsync(
            [Item(1, "A", 1), Item(2, "B", 1)], [],
            TestContext.Current.CancellationToken);

        Assert.Equal(50m, result.CommercialMinimum);
        Assert.Equal(70m, result.CommercialExpected);
        Assert.Equal(90m, result.CommercialMaximum);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public async Task PriceAsync_MultipliesUnitRangeByQuantityExactlyOnce()
    {
        var service = Service(Priceable(
            1, "A", 100m, 120m, 140m, 0.80m, quantity: 4));

        var result = await service.PriceAsync(
            [Item(1, "A", 4)], [], TestContext.Current.CancellationToken);

        Assert.Equal(480m, result.CommercialExpected);
        Assert.Equal(120m, result.Items[0].UnitExpected);
        Assert.Equal(480m, result.Items[0].LineExpected);
    }

    [Fact]
    public async Task PriceAsync_WithMultipleQuantities_SumsLineRanges()
    {
        var result = await Service(
                Priceable(1, "A", 90m, 100m, 110m, 0.80m, quantity: 4),
                Priceable(2, "B", 180m, 200m, 220m, 0.80m, quantity: 2),
                Priceable(3, "C", 270m, 300m, 330m, 0.80m, quantity: 1))
            .PriceAsync(
                [Item(1, "A", 4), Item(2, "B", 2), Item(3, "C", 1)],
                [],
                TestContext.Current.CancellationToken);

        Assert.Equal(1_100m, result.CommercialExpected);
        Assert.Equal([400m, 400m, 300m],
            result.Items.Select(item => item.LineExpected));
    }

    [Fact]
    public async Task PriceAsync_WithUnknownQuantity_DoesNotAssumeOne()
    {
        var result = await Service(Priceable(
                1, "A", 100m, 120m, 140m, 0.80m, quantity: null))
            .PriceAsync(
                [Item(1, "A", null)], [],
                TestContext.Current.CancellationToken);

        Assert.Equal(0, result.PricedItemCount);
        Assert.Equal(1, result.NotPriceableItemCount);
        Assert.Null(result.CommercialExpected);
        Assert.Null(result.Items[0].LineExpected);
    }

    [Fact]
    public async Task PriceAsync_WeightsConfidenceByLineExpected()
    {
        var result = await Service(
                Priceable(1, "A", 90m, 100m, 110m, 0.90m, quantity: 10),
                Priceable(2, "B", 810m, 900m, 990m, 0.70m, quantity: 1))
            .PriceAsync(
                [Item(1, "A", 10), Item(2, "B", 1)], [],
                TestContext.Current.CancellationToken);

        Assert.Equal(1_530m / 1_900m, result.ConfidenceScore);
    }

    [Fact]
    public async Task PriceAsync_WithAllNotPriceable_DoesNotReturnZeroPrices()
    {
        var result = await Service(
                NotPriceable(1, "A"),
                NotPriceable(2, "B"))
            .PriceAsync(
                [Item(1, "A", 1), Item(2, "B", 1)], [],
                TestContext.Current.CancellationToken);

        Assert.Equal(0, result.PricedItemCount);
        Assert.Equal(2, result.NotPriceableItemCount);
        Assert.Null(result.CommercialMinimum);
        Assert.Null(result.CommercialExpected);
        Assert.Null(result.CommercialMaximum);
        Assert.Equal(HistoricalPriceConfidenceLevel.Low, result.ConfidenceLevel);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task PriceAsync_UsesExpectedEconomicWeightForConfidence()
    {
        var result = await Service(
                Priceable(1, "A", 90m, 100m, 110m, 0.90m),
                Priceable(2, "B", 810m, 900m, 990m, 0.30m))
            .PriceAsync(
                [Item(1, "A", 1), Item(2, "B", 1)], [],
                TestContext.Current.CancellationToken);

        Assert.Equal(0.36m, result.ConfidenceScore);
        Assert.Equal(HistoricalPriceConfidenceLevel.Medium, result.ConfidenceLevel);
    }

    [Fact]
    public async Task PriceAsync_WithMaterialReviewShare_CapsConfidenceAndRequiresReview()
    {
        var result = await Service(
                Priceable(1, "A", 100m, 100m, 100m, 0.90m, requiresReview: true),
                Priceable(2, "B", 100m, 100m, 100m, 0.90m),
                Priceable(3, "C", 100m, 100m, 100m, 0.90m),
                Priceable(4, "D", 100m, 100m, 100m, 0.90m))
            .PriceAsync(
                [Item(1, "A", 1), Item(2, "B", 1), Item(3, "C", 1), Item(4, "D", 1)],
                [],
                TestContext.Current.CancellationToken);

        Assert.Equal(0.59m, result.ConfidenceScore);
        Assert.Equal(HistoricalPriceConfidenceLevel.Medium, result.ConfidenceLevel);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task PriceAsync_DeduplicatesAssumptionsMissingDataAndWarnings()
    {
        var service = Service(
            Priceable(1, "A", 10m, 20m, 30m, 0.70m,
                assumptions: ["ASSUMPTION"], missing: ["MISSING"], warnings: ["WARNING"]),
            Priceable(2, "B", 10m, 20m, 30m, 0.70m,
                assumptions: ["ASSUMPTION"], missing: ["MISSING"], warnings: ["WARNING"]));

        var result = await service.PriceAsync(
            [Item(1, "A", 1), Item(2, "B", 1)], [],
            TestContext.Current.CancellationToken);

        Assert.Equal(["ASSUMPTION"], result.Assumptions);
        Assert.Equal(["MISSING"], result.MissingData);
        Assert.Equal(["WARNING"], result.Warnings);
    }

    [Fact]
    public async Task PriceAsync_WithIncompatibleCurrencies_RejectsAggregate()
    {
        var service = Service(
            Priceable(1, "A", 10m, 20m, 30m, 0.70m),
            Priceable(2, "B", 10m, 20m, 30m, 0.70m, currency: "USD"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PriceAsync(
            [Item(1, "A", 1), Item(2, "B", 1)], [],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PriceAsync_WhenOneItemFails_ContinuesAndReportsTechnicalFailure()
    {
        var service = Service(
            Priceable(1, "A", 10m, 20m, 30m, 0.70m),
            new InvalidOperationException("AI2 unavailable"),
            Priceable(3, "C", 40m, 50m, 60m, 0.70m));

        var result = await service.PriceAsync(
            [Item(1, "A", 1), Item(2, "B", 1), Item(3, "C", 1)], [],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.PricedItemCount);
        Assert.Equal(1, result.NotPriceableItemCount);
        Assert.Equal(70m, result.CommercialExpected);
        Assert.Equal(
            RequirementElementPricingStatus.TechnicalFailure,
            result.Items[1].Status);
        Assert.Equal("PRICING_TECHNICAL_FAILURE", result.Items[1].FailureCode);
        Assert.Equal("AI2 unavailable", result.Items[1].FailureMessage);
    }

    [Fact]
    public async Task PriceAsync_WithEmptyItems_ReturnsEmptyControlledAggregate()
    {
        var result = await Service().PriceAsync(
            [], [], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ItemCount);
        Assert.Null(result.CommercialExpected);
        Assert.Null(result.Currency);
        Assert.False(result.IsPartial);
        Assert.False(result.RequiresReview);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PriceAsync_WithCasaPsShape_PreservesDuplicatesAndContinues()
    {
        var result = await Service(
                Priceable(1, "V-5", 90m, 100m, 110m, 0.80m, quantity: 2),
                Priceable(2, "PV-6", 180m, 200m, 220m, 0.80m, quantity: 2),
                Priceable(3, "C-1", 270m, 300m, 330m, 0.70m, quantity: 2),
                Priceable(4, "V-4", 360m, 400m, 440m, 0.70m),
                Priceable(5, "V-4", 450m, 500m, 550m, 0.70m),
                NotPriceable(6, "C-4"))
            .PriceAsync(
                [
                    Item(1, "V-5", 2),
                    Item(2, "PV-6", 2),
                    Item(3, "C-1", 2),
                    Item(4, "V-4", 1),
                    Item(5, "V-4", 1),
                    Item(6, "C-4", 2)
                ],
                [],
                TestContext.Current.CancellationToken);

        Assert.Equal(6, result.ItemCount);
        Assert.Equal(5, result.PricedItemCount);
        Assert.Equal(1, result.NotPriceableItemCount);
        Assert.Equal(2_100m, result.CommercialExpected);
        Assert.Equal(2, result.Items.Count(item => item.Reference == "V-4"));
        Assert.Equal(2, result.Items[0].Quantity);
        Assert.Equal(200m, result.Items[0].LineExpected);
        Assert.True(result.IsPartial);
        Assert.True(result.RequiresReview);
    }

    private static PriceRequirementExtractionService Service(params object[] outcomes) =>
        new(
            new FakeElementPricingService(outcomes),
            NullLogger<PriceRequirementExtractionService>.Instance);

    private static StructuredItemData Item(int sequence, string reference, int? quantity) =>
        new(
            sequence,
            reference,
            reference,
            StructuredElementType.Door,
            "1000 x 1000",
            1000,
            1000,
            quantity,
            false,
            [],
            [],
            [],
            null,
            null,
            1m,
            "CORREDIZA",
            0.90m,
            CanonicalExtractionValueStatus.Explicit);

    private static PricedRequirementElement Priceable(
        int id,
        string reference,
        decimal minimum,
        decimal expected,
        decimal maximum,
        decimal confidence,
        bool requiresReview = false,
        string currency = "COP",
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<string>? missing = null,
        IReadOnlyList<string>? warnings = null,
        decimal? quantity = 1m)
    {
        var level = confidence switch
        {
            < 0.35m => HistoricalPriceConfidenceLevel.Low,
            < 0.60m => HistoricalPriceConfidenceLevel.Medium,
            < 0.80m => HistoricalPriceConfidenceLevel.Good,
            _ => HistoricalPriceConfidenceLevel.High
        };
        var technical = Technical(
            currency,
            minimum,
            expected,
            maximum,
            confidence,
            level,
            requiresReview,
            assumptions ?? [],
            missing ?? []);
        var commercial = Commercial(technical);
        return new PricedRequirementElement(
            id,
            reference,
            Query(id, quantity),
            technical,
            commercial,
            warnings ?? [],
            requiresReview);
    }

    private static PricedRequirementElement NotPriceable(int id, string reference)
    {
        var technical = Technical(
            "COP", null, null, null, 0m, HistoricalPriceConfidenceLevel.Low,
            true, [], ["NO_COMPARABLES"]);
        return new PricedRequirementElement(
            id,
            reference,
            Query(id),
            technical,
            Commercial(technical),
            [],
            true);
    }

    private static HistoricalCandidateQuery Query(int id, decimal? quantity = 1m) =>
        new("PUERTA", "3831", "TEMPLADO", 6m, "CORREDIZA", 1000,
            1000, 1m, null, quantity, 5);

    private static HistoricalTechnicalPriceEstimate Technical(
        string currency,
        decimal? minimum,
        decimal? expected,
        decimal? maximum,
        decimal confidence,
        HistoricalPriceConfidenceLevel level,
        bool requiresReview,
        IReadOnlyList<string> assumptions,
        IReadOnlyList<string> missing) =>
        new(
            currency,
            minimum,
            expected,
            maximum,
            confidence,
            level,
            "HISTORICAL_COMPARABLES",
            5,
            5,
            1,
            requiresReview,
            assumptions,
            missing,
            [],
            []);

    private static HistoricalCommercialPriceEstimate Commercial(
        HistoricalTechnicalPriceEstimate technical) =>
        new(
            technical.Currency,
            technical.PricingSource,
            HistoricalPricingBasis.PublicQuotedItemPrices,
            technical.Minimum,
            technical.Expected,
            technical.Maximum,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            technical.Minimum,
            technical.Expected,
            technical.Maximum,
            technical.ConfidenceScore,
            technical.ConfidenceLevel,
            technical.RequiresReview,
            technical.Assumptions,
            technical.MissingData);

    private sealed class FakeElementPricingService(params object[] outcomes)
        : IPriceRequirementElementService
    {
        private int _index;

        public Task<PricedRequirementElement> PriceAsync(
            StructuredItemData item,
            IReadOnlyList<ProcessingWarningData> warnings,
            CancellationToken cancellationToken = default)
        {
            var outcome = outcomes[_index++];
            return outcome switch
            {
                PricedRequirementElement priced => Task.FromResult(priced),
                Exception exception => Task.FromException<PricedRequirementElement>(exception),
                _ => throw new InvalidOperationException("Unsupported fake outcome.")
            };
        }
    }
}
