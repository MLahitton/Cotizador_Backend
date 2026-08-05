using Domain.Catalogs;
using Xunit;

namespace CotizadorBackend.Tests.Domain.Catalogs;

public sealed class CanonicalCatalogTests
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProductSystem_NormalizesCodeAndPersistsPricingFlags()
    {
        var value = ProductSystem.Create(
            " baranda ",
            "Sistema para barandas",
            true,
            false,
            true,
            true,
            At);

        Assert.Equal("BARANDA", value.Code);
        Assert.True(value.ActiveForRecognition);
        Assert.False(value.Priceable);
        Assert.True(value.FuturePriceable);
        Assert.True(value.RequiresReview);
        Assert.True(value.IsActive);
    }

    [Fact]
    public void CatalogAlias_NormalizesAccentsAndRepeatedSpaces()
    {
        var value = CatalogAlias.Create(
            CatalogAliasCategory.System,
            "  Venecia   Serie 50 ",
            "k50",
            CatalogAliasMatchPolicy.TechnicalPhrase,
            true,
            1.0m,
            At);

        Assert.Equal("VENECIA SERIE 50", value.NormalizedAlias);
        Assert.Equal("K50", value.CanonicalCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("40")]
    [InlineData("50")]
    [InlineData("70")]
    public void CatalogAlias_RejectsEmptyOrNumericAlias(string alias)
    {
        Assert.Throws<ArgumentException>(() => CatalogAlias.Create(
            CatalogAliasCategory.System,
            alias,
            "K40",
            CatalogAliasMatchPolicy.ExactNormalized,
            false,
            1.0m,
            At));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void CatalogAlias_RejectsInvalidConfidence(decimal confidence)
    {
        Assert.Throws<ArgumentException>(() => CatalogAlias.Create(
            CatalogAliasCategory.System,
            "VENECIA SERIE 40",
            "K40",
            CatalogAliasMatchPolicy.TechnicalPhrase,
            true,
            confidence,
            At));
    }
}
