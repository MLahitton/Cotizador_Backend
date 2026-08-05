using Domain.Catalogs;
using Xunit;

namespace CotizadorBackend.Tests.Domain.Catalogs;

public sealed class GlassCatalogTests
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GlassType_RequiresCode(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            GlassType.Create(value!, "Glass", null, At));
    }

    [Fact]
    public void GlassType_NormalizesCodeNameAndOptionalDescription()
    {
        var value = GlassType.Create(
            " lam_4_4 ",
            " Laminado 4+4 ",
            "   ",
            At);

        Assert.Equal("LAM_4_4", value.Code);
        Assert.Equal("Laminado 4+4", value.Name);
        Assert.Null(value.Description);
        Assert.True(value.IsActive);
        Assert.Null(value.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GlassType_RequiresName(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            GlassType.Create("LAM_4_4", value!, null, At));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(1, 2, 1, 2)]
    [InlineData(1, 1, 3, 2)]
    public void PriceRange_RejectsInvalidNumbers(
        int version,
        decimal minimum,
        decimal expected,
        decimal maximum)
    {
        Assert.Throws<ArgumentException>(() => CreateRange(
            version,
            minimum,
            expected,
            maximum,
            "COP"));
    }

    [Theory]
    [InlineData("CO")]
    [InlineData("C0P")]
    [InlineData("COPP")]
    public void PriceRange_RejectsInvalidCurrency(string currency)
    {
        Assert.Throws<ArgumentException>(() => CreateRange(
            1,
            1,
            1,
            1,
            currency));
    }

    [Fact]
    public void PriceRange_NormalizesCurrencyAndAllowsFixedRange()
    {
        var value = CreateRange(1, 95000m, 95000m, 95000m, " cop ");

        Assert.Equal("COP", value.Currency);
        Assert.Equal(95000m, value.MinimumPricePerSquareMeter);
        Assert.Equal(95000m, value.ExpectedAmountPerM2);
        Assert.Equal(95000m, value.MaximumPricePerSquareMeter);
        Assert.Equal(GlassPriceRangeStatus.Preliminary, value.Status);
        Assert.Null(value.ValidToUtc);
    }

    [Fact]
    public void PriceRange_RequiresFinalDateAfterInitialDate()
    {
        Assert.Throws<ArgumentException>(() =>
            GlassPriceRangeVersion.Create(
                Guid.NewGuid(), 1, 1, 1, 1, "COP",
                GlassPriceRangeStatus.Preliminary,
                At, At, At));
    }

    [Fact]
    public void PriceRange_RejectsUndefinedStatus()
    {
        Assert.Throws<ArgumentException>(() =>
            GlassPriceRangeVersion.Create(
                Guid.NewGuid(), 1, 1, 1, 1, "COP",
                (GlassPriceRangeStatus)999,
                At, null, At));
    }

    private static GlassPriceRangeVersion CreateRange(
        int version,
        decimal minimum,
        decimal expected,
        decimal maximum,
        string currency) =>
        GlassPriceRangeVersion.Create(
            Guid.NewGuid(),
            version,
            minimum,
            expected,
            maximum,
            currency,
            GlassPriceRangeStatus.Preliminary,
            At,
            null,
            At);
}
