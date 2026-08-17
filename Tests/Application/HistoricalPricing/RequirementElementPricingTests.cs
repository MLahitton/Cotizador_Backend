using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.HistoricalPricing;

public sealed class RequirementElementPricingTests
{
    private readonly RequirementElementToHistoricalPricingMapper _mapper = new();

    [Fact]
    public void Map_WithPv06CanonicalItem_ProducesEquivalentHistoricalQuery()
    {
        var mapping = _mapper.Map(Item(), []);

        Assert.Equal(6, mapping.ElementId);
        Assert.Equal("PV-06", mapping.Reference);
        Assert.Equal("PUERTA", mapping.CandidateQuery.Category);
        Assert.Equal("3831", mapping.CandidateQuery.System);
        Assert.Equal("TEMPLADO", mapping.CandidateQuery.Glass);
        Assert.Equal(6m, mapping.CandidateQuery.GlassThickness);
        Assert.Null(mapping.CandidateQuery.GlassComposition);
        Assert.Equal("CORREDIZA", mapping.CandidateQuery.Configuration);
        Assert.Equal(3740m, mapping.CandidateQuery.Width);
        Assert.Equal(2500m, mapping.CandidateQuery.Height);
        Assert.Equal(9.35m, mapping.CandidateQuery.Area);
        Assert.Equal(1m, mapping.CandidateQuery.Quantity);
        Assert.Equal("NEGRO PINTURA AL HORNO", mapping.CandidateQuery.Finish);
        Assert.Equal(0.96m, mapping.ExtractionConfidence);
        Assert.False(mapping.RequiresReview);
    }

    [Fact]
    public void Map_WithMissingSystem_DoesNotInventSystem()
    {
        var mapping = _mapper.Map(Item(technical: Technical(
            system: null,
            systemSource: TechnicalClassificationSource.Unresolved)), []);

        Assert.Null(mapping.CandidateQuery.System);
        Assert.Contains("SYSTEM_UNKNOWN", mapping.MappingWarnings);
    }

    [Fact]
    public void Map_WithMissingGlass_DoesNotInventGlassData()
    {
        var mapping = _mapper.Map(Item(withoutGlass: true), []);

        Assert.Null(mapping.CandidateQuery.Glass);
        Assert.Null(mapping.CandidateQuery.GlassThickness);
        Assert.Null(mapping.CandidateQuery.GlassComposition);
    }

    [Fact]
    public void Map_WithInferredSystem_UsesValueAndAddsWarning()
    {
        var mapping = _mapper.Map(Item(technical: Technical(
            system: "3831",
            systemSource: TechnicalClassificationSource.Inferred)), []);

        Assert.Equal("3831", mapping.CandidateQuery.System);
        Assert.Contains("SYSTEM_INFERRED", mapping.MappingWarnings);
    }

    [Fact]
    public void Map_WithAmbiguousItem_KeepsAvailableCategoryAndRequiresReview()
    {
        var mapping = _mapper.Map(Item(
            status: CanonicalExtractionValueStatus.Ambiguous), []);

        Assert.Equal("PUERTA", mapping.CandidateQuery.Category);
        Assert.Contains("ITEM_AMBIGUOUS", mapping.MappingWarnings);
        Assert.True(mapping.RequiresReview);
    }

    [Fact]
    public void Map_WithQuantityGreaterThanOne_DoesNotMultiplyReportedArea()
    {
        var mapping = _mapper.Map(Item(quantity: 4, area: 9.35m), []);

        Assert.Equal(4m, mapping.CandidateQuery.Quantity);
        Assert.Equal(9.35m, mapping.CandidateQuery.Area);
    }

    [Fact]
    public void Map_WithAreaMismatchWarning_PreservesReportedAreaAndRequiresReview()
    {
        ProcessingWarningData[] warnings = [new(
            "MEASUREMENT_AREA_MISMATCH", "Area reportada diferente.", [])];

        var mapping = _mapper.Map(Item(area: 9.35m), warnings);

        Assert.Equal(9.35m, mapping.CandidateQuery.Area);
        Assert.Contains("MEASUREMENT_AREA_MISMATCH", mapping.MappingWarnings);
        Assert.True(mapping.RequiresReview);
    }

    [Fact]
    public void Map_WithMissingFinish_DoesNotInventFinish()
    {
        var mapping = _mapper.Map(Item(technical: Technical(finish: null)), []);

        Assert.Null(mapping.CandidateQuery.Finish);
    }

    [Fact]
    public void Map_WithLaminatedCanonicalGlass_PreservesComposition()
    {
        var mapping = _mapper.Map(Item(glass: Glass("LAM_4_4")), []);

        Assert.Equal("LAMINADO", mapping.CandidateQuery.Glass);
        Assert.Equal(4m, mapping.CandidateQuery.GlassThickness);
        Assert.Equal("4+4", mapping.CandidateQuery.GlassComposition);
    }

    [Fact]
    public async Task PriceAsync_UsesTechnicalPipelineOnceAndBuildsPublicCommercialEstimate()
    {
        var technicalEstimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();
        var technical = TechnicalEstimate();
        technicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(technical);
        var service = new PriceRequirementElementService(
            _mapper,
            technicalEstimator,
            new HistoricalCommercialPriceEstimator(technicalEstimator));

        var result = await service.PriceAsync(
            Item(), [], TestContext.Current.CancellationToken);

        await technicalEstimator.Received(1).EstimateAsync(
            Arg.Is<HistoricalCandidateQuery>(query =>
                query != null && query.System == "3831" && query.Area == 9.35m),
            TestContext.Current.CancellationToken);
        Assert.Same(technical, result.TechnicalEstimate);
        Assert.Equal(
            HistoricalPricingBasis.PublicQuotedItemPrices,
            result.CommercialEstimate.PricingBasis);
        Assert.Equal(technical.Expected, result.CommercialEstimate.FinalExpected);
        Assert.Equal(technical.Expected, result.UnitExpected);
        Assert.Equal(technical.Expected, result.LineExpected);
    }

    [Fact]
    public async Task PriceAsync_WithQuantityFour_SeparatesUnitAndLinePriceOnce()
    {
        var technicalEstimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();
        var technical = TechnicalEstimate() with
        {
            Minimum = 700_000m,
            Expected = 730_972.4684856656m,
            Maximum = 760_000m
        };
        technicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(technical);
        var service = new PriceRequirementElementService(
            _mapper,
            technicalEstimator,
            new HistoricalCommercialPriceEstimator(technicalEstimator));

        var result = await service.PriceAsync(
            Item(quantity: 4), [], TestContext.Current.CancellationToken);

        Assert.Equal(4m, result.Quantity);
        Assert.Equal(730_972.4684856656m, result.UnitExpected);
        Assert.Equal(2_923_889.8739426624m, result.LineExpected);
        Assert.Equal(9.35m, result.CandidateQuery.Area);
        Assert.Equal(
            HistoricalPricingBasis.PublicQuotedItemPrices,
            result.CommercialEstimate.PricingBasis);
        Assert.Equal(0m, result.CommercialEstimate.AdministrationExpected);
        Assert.Equal(0m, result.CommercialEstimate.ContingencyExpected);
        Assert.Equal(0m, result.CommercialEstimate.ProfitExpected);
        Assert.Equal(0m, result.CommercialEstimate.VatOnProfitExpected);
    }

    private static StructuredItemData Item(
        StructuredItemTechnicalClassificationData? technical = null,
        StructuredItemGlassData? glass = null,
        int quantity = 1,
        decimal area = 9.35m,
        CanonicalExtractionValueStatus status = CanonicalExtractionValueStatus.Explicit,
        bool withoutGlass = false) =>
        new(
            6,
            "PV-06",
            "Puerta vidriera",
            StructuredElementType.Door,
            "3740 x 2500",
            3740,
            2500,
            quantity,
            false,
            [],
            [],
            [],
            withoutGlass ? null : glass ?? Glass("TEMP_6"),
            technical ?? Technical(),
            area,
            "CORREDIZA",
            0.96m,
            status);

    private static StructuredItemGlassData Glass(string code) =>
        new(
            code,
            code,
            GlassAssignmentScope.Item,
            false,
            [],
            [],
            []);

    private static StructuredItemTechnicalClassificationData Technical(
        string? system = "3831",
        TechnicalClassificationSource? systemSource = TechnicalClassificationSource.Explicit,
        string? finish = "NEGRO PINTURA AL HORNO") =>
        new(
            system,
            system,
            systemSource,
            0.95m,
            null,
            null,
            null,
            null,
            finish,
            finish,
            finish is null ? null : TechnicalClassificationSource.Explicit,
            finish is null ? null : 0.95m,
            false,
            []);

    private static HistoricalTechnicalPriceEstimate TechnicalEstimate() =>
        new(
            "COP",
            7_900_000m,
            8_700_000m,
            9_300_000m,
            0.59m,
            HistoricalPriceConfidenceLevel.Medium,
            "HISTORICAL_COMPARABLES",
            5,
            5,
            0,
            true,
            [],
            [],
            [],
            []);
}
