using Domain.Catalogs;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class StructuredItemGlassValuationTests
{
    public static TheoryData<int, int, int, decimal, decimal, decimal,
        decimal, decimal, decimal> ExactCases => new()
    {
        { 1500, 1000, 3, 90000m, 110000m, 1.5m, 4.5m, 405000m, 495000m },
        { 2100, 1400, 4, 95000m, 95000m, 2.94m, 11.76m, 1117200m, 1117200m },
        { 6200, 3300, 1, 120000m, 140000m, 20.46m, 20.46m, 2455200m, 2864400m },
        { 3800, 1100, 2, 125000m, 145000m, 4.18m, 8.36m, 1045000m, 1212200m }
    };

    [Theory, MemberData(nameof(ExactCases))]
    public void Calculate_ReturnsExactDecimalResults(
        int width, int height, int quantity, decimal minimum, decimal maximum,
        decimal unitArea, decimal totalArea, decimal minimumAmount,
        decimal maximumAmount)
    {
        var result = Calculate(width, height, quantity, minimum, maximum);
        Assert.Equal(unitArea, result.UnitAreaSquareMeters);
        Assert.Equal(totalArea, result.TotalAreaSquareMeters);
        Assert.Equal(minimumAmount, result.MinimumAmount);
        Assert.Equal(maximumAmount, result.MaximumAmount);
        Assert.Equal(GlassValuationStatus.Valued, result.Status);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Calculate_RoundsOnlyFinalAmountAwayFromZero()
    {
        var result = Calculate(1, 500000, 1, 1.01m, 1.01m);
        Assert.Equal(0.5m, result.UnitAreaSquareMeters);
        Assert.Equal(0.51m, result.MinimumAmount);
    }

    [Theory]
    [InlineData(0, 1, 1)] [InlineData(-1, 1, 1)]
    [InlineData(1, 0, 1)] [InlineData(1, -1, 1)]
    [InlineData(1, 1, 0)] [InlineData(1, 1, -1)]
    public void Calculate_InvalidDimensionsOrQuantity_ThrowsArgumentException(
        int width, int height, int quantity) =>
        Assert.Throws<ArgumentException>(() =>
            Calculate(width, height, quantity, 1m, 1m));

    [Theory]
    [InlineData(0, 1)] [InlineData(-1, 1)] [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Calculate_InvalidPrices_ThrowsArgumentException(
        decimal minimum, decimal maximum) =>
        Assert.Throws<ArgumentException>(() =>
            Calculate(1, 1, 1, minimum, maximum));

    private static StructuredItemGlassValuationInput Calculate(
        int width, int height, int quantity, decimal minimum, decimal maximum) =>
        StructuredExtractionItemGlassValuation.Calculate(
            width, height, quantity, Guid.NewGuid(), Guid.NewGuid(), 1,
            GlassPriceRangeStatus.Preliminary, "COP", minimum, maximum);
}
