using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class StructuredItemGlassValuationStateTests
{
    public static TheoryData<GlassValuationReason> Reasons => new()
    {
        GlassValuationReason.MissingMeasurements,
        GlassValuationReason.MissingQuantity,
        GlassValuationReason.GlassNotNormalized,
        GlassValuationReason.GlassTypeNotResolved,
        GlassValuationReason.PriceRangeNotAvailable,
        GlassValuationReason.CurrencyMismatch
    };

    [Theory, MemberData(nameof(Reasons))]
    public void Create_NotValued_PreservesReasonWithoutInventingAmounts(
        GlassValuationReason reason)
    {
        var input = new StructuredItemGlassValuationInput(
            GlassValuationStatus.NotValued, reason,
            reason == GlassValuationReason.PriceRangeNotAvailable
                ? Guid.NewGuid() : null,
            null, null, null, null, null, null, null, null, null, null,
            null, null);

        var valuation = CreateExtraction(input).Items.Single().GlassValuation;

        Assert.NotNull(valuation);
        Assert.Equal(GlassValuationStatus.NotValued, valuation.Status);
        Assert.Equal(reason, valuation.Reason);
        Assert.Null(valuation.GlassPriceRangeVersionId);
        Assert.Null(valuation.MinimumAmount);
        Assert.Null(valuation.MaximumAmount);
    }

    [Fact]
    public void Create_Valued_HasNoReasonAndHasAmounts()
    {
        var input = StructuredExtractionItemGlassValuation.Calculate(
            1500, 1000, 3, Guid.NewGuid(), Guid.NewGuid(), 1,
            global::Domain.Catalogs.GlassPriceRangeStatus.Preliminary,
            "COP", 90000m, 100000m, 110000m);

        var valuation = CreateExtraction(input).Items.Single().GlassValuation;

        Assert.NotNull(valuation);
        Assert.Equal(GlassValuationStatus.Valued, valuation.Status);
        Assert.Null(valuation.Reason);
        Assert.Equal(405000m, valuation.MinimumAmount);
        Assert.Equal(450000m, valuation.ExpectedAmount);
        Assert.Equal(495000m, valuation.MaximumAmount);
        Assert.Equal(TimeSpan.Zero, valuation.CalculatedAtUtc.Offset);
    }

    private static StructuredDocumentExtraction CreateExtraction(
        StructuredItemGlassValuationInput valuation) =>
        StructuredDocumentExtraction.Create(
            Guid.NewGuid(), StructuredExtractionStatus.Completed,
            "Project", null, null, 1, 0, 0, 1,
            "rule_based_v2", 1,
            [new StructuredItemInput(
                1, "V-01", "Window", StructuredElementType.Window,
                null, 1, 1, 1, false, null, valuation)],
            [], [], [], [],
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
