using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PreQuotePreliminaryPricingTests
{
    [Fact]
    public void TryCalculate_UsesRealAreaWhenAboveMinimumBillableArea()
    {
        var result = Calculate(
            width: 1500,
            height: 1000,
            quantity: 1,
            elementType: StructuredElementType.Window,
            finishCode: "STANDARD_NATURAL");

        Assert.NotNull(result);
        Assert.Equal(1.5m, result.BillableAreaUnitSquareMeters);
        Assert.Equal(135000m, result.GlassMinimumAmount);
        Assert.Equal(150000m, result.GlassExpectedAmount);
        Assert.Equal(165000m, result.GlassMaximumAmount);
    }

    [Fact]
    public void TryCalculate_UsesMinimumBillableAreaWhenRealAreaIsLower()
    {
        var result = Calculate(
            width: 500,
            height: 500,
            quantity: 3,
            elementType: StructuredElementType.Window,
            finishCode: "STANDARD_NATURAL");

        Assert.NotNull(result);
        Assert.Equal(1.00m, result.BillableAreaUnitSquareMeters);
        Assert.Equal(3.00m, result.GlassMinimumAmount / 90000m);
        Assert.Equal(300000m, result.GlassExpectedAmount);
    }

    [Fact]
    public void TryCalculate_AppliesBlackMatteFinishAndMediumAccessories()
    {
        var result = Calculate(
            width: 1000,
            height: 1000,
            quantity: 1,
            elementType: StructuredElementType.Window,
            finishCode: "BLACK_MATTE");

        Assert.NotNull(result);
        Assert.Equal("PREQUOTE_V1_2026_08", result.PricingProfileVersion);
        Assert.Equal(1.08m, result.FinishFactorMinimum);
        Assert.Equal(1.12m, result.FinishFactorExpected);
        Assert.Equal(1.15m, result.FinishFactorMaximum);
        Assert.Equal(0.08m, result.AccessoryFactor);
        Assert.Equal("COP", PreQuotePreliminaryPricing.Currency);
    }

    [Fact]
    public void TryCalculate_UnknownFinishAddsMissingDataAndReview()
    {
        var result = Calculate(
            width: 1000,
            height: 1000,
            quantity: 1,
            elementType: StructuredElementType.Window,
            finishCode: null);

        Assert.NotNull(result);
        Assert.Contains("FINISH_NOT_CONFIRMED", result.MissingData);
        Assert.Contains("UNKNOWN_FINISH_FACTOR_APPLIED", result.Assumptions);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public void TryCalculate_NonValuedSourceDoesNotInventZero()
    {
        var source = new PreQuoteDraftItemValuationSnapshotSource(
            Guid.NewGuid(),
            PreQuoteDraftValuationStatus.RequiresReview,
            GlassValuationReason.PriceRangeNotAvailable,
            Guid.NewGuid(),
            null,
            1000,
            1000,
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            null,
            null);

        var result = PreQuotePreliminaryPricing.TryCalculate(
            StructuredElementType.Window,
            "Ventana",
            source,
            null);

        Assert.Null(result);
    }

    private static PreQuotePreliminaryPricingResult Calculate(
        int width,
        int height,
        int quantity,
        StructuredElementType elementType,
        string? finishCode)
    {
        var source = new PreQuoteDraftItemValuationSnapshotSource(
            Guid.NewGuid(),
            PreQuoteDraftValuationStatus.Valued,
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            width,
            height,
            quantity,
            (decimal)width * height / 1_000_000m,
            (decimal)width * height / 1_000_000m * quantity,
            90000m,
            90000m,
            110000m,
            "COP",
            DateTimeOffset.UnixEpoch,
            null,
            null,
            1,
            100000m,
            100000m,
            110000m);

        var technical = PreQuoteDraftItemTechnicalSnapshot.Create(
            Guid.NewGuid(),
            new PreQuoteDraftItemTechnicalSnapshotSource(
                Guid.NewGuid(),
                "K50",
                "K50",
                TechnicalClassificationSource.Explicit,
                1m,
                "MARCO_47",
                "SG0047",
                TechnicalClassificationSource.Alias,
                1m,
                finishCode,
                finishCode,
                finishCode is null ? null : TechnicalClassificationSource.Explicit,
                finishCode is null ? null : 1m,
                false,
                []));

        return PreQuotePreliminaryPricing.TryCalculate(
            elementType,
            "Ventana corrediza",
            source,
            technical)!;
    }
}
