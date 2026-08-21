using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GlassCandidateResolverTests
{
    private static readonly GlassCandidateResolver Resolver = new();
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(5, "TEMP_5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC")]
    [InlineData(6, "TEMP_6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC")]
    [InlineData(8, "TEMP_8", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC")]
    [InlineData(10, "TEMP_10", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC")]
    public void Resolve_TemperedWithThickness_ReturnsExactCatalogGlass(
        decimal thickness,
        string expectedCode,
        string expectedDisplayName)
    {
        var result = Resolver.Resolve(
            Input(type: "templado", thickness: thickness, color: "incoloro"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(1m, result.Confidence);
        Assert.NotNull(result.Suggested);
        Assert.Equal(expectedCode, result.Suggested!.Code);
        Assert.Equal(expectedDisplayName, result.Suggested.DisplayName);
        Assert.Contains(
            GlassResolutionReasonCodes.ThicknessMatched,
            result.ResolutionReasons);
    }

    [Theory]
    [InlineData(
        4,
        "INC",
        "LAM_4_4",
        "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC")]
    [InlineData(
        5,
        "INC",
        "LAM_5_5",
        "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC")]
    [InlineData(
        4,
        "GRIS",
        "LAM_4_4_GRAY",
        "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM GRIS + 4 MM INC")]
    [InlineData(
        5,
        "GRIS",
        "LAM_5_5_GRAY",
        "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM GRIS + 5 MM INC")]
    public void Resolve_LaminatedWithComponents_ReturnsExactCatalogGlass(
        decimal thickness,
        string color,
        string expectedCode,
        string expectedDisplayName)
    {
        var result = Resolver.Resolve(
            Input(
                raw: $"Laminado crudo {thickness} + PVB 0.38 {color} + {thickness}",
                type: "laminado",
                thickness: thickness,
                color: color,
                composition: "laminado"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(expectedCode, result.Suggested!.Code);
        Assert.Equal(expectedDisplayName, result.Suggested.DisplayName);
    }

    [Fact]
    public void Resolve_LaminatedWithoutThickness_ReturnsAlternativesAndReview()
    {
        var result = Resolver.Resolve(
            Input(type: "laminado"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            GlassResolutionReviewReasons.GlassAmbiguous,
            result.ReviewReasons);
        Assert.Contains(result.Alternatives, value => value.Code == "LAM_4_4");
        Assert.Contains(result.Alternatives, value => value.Code == "LAM_5_5");
    }

    [Fact]
    public void Resolve_MissingGlass_ReturnsReviewWithoutSuggested()
    {
        var result = Resolver.Resolve(Input(), Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            GlassResolutionReviewReasons.GlassNotSpecified,
            result.ReviewReasons);
    }

    [Fact]
    public void Resolve_UnknownGlass_ReturnsReview()
    {
        var result = Resolver.Resolve(
            Input(type: "vidrio extraterrestre", thickness: 7),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            GlassResolutionReviewReasons.GlassNoCompatibleCandidate,
            result.ReviewReasons);
    }

    [Fact]
    public void Resolve_ConflictingFamilySignals_DoesNotSelect()
    {
        var result = Resolver.Resolve(
            Input(type: "templado", composition: "laminado"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            GlassResolutionReviewReasons.GlassConflictingSignals,
            result.ReviewReasons);
    }

    [Theory]
    [InlineData("crudo 5 mm incoloro", "RAW_5_INC")]
    [InlineData("crudo 4 mm mini boreal", "RAW_4_MINI_BOREAL")]
    [InlineData("templado 4 mm", "TEMP_4")]
    public void Resolve_MonolithicHistoricalBdGnOptions_ReturnsExactCatalogGlass(
        string raw,
        string expectedCode)
    {
        var result = Resolver.Resolve(Input(raw: raw), ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(expectedCode, result.Suggested!.Code);
    }

    [Fact]
    public void Resolve_RawGlassDoesNotResolveToTemperedByThicknessOnly()
    {
        var result = Resolver.Resolve(
            Input(raw: "crudo 5 mm incoloro"),
            ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("RAW_5_INC", result.Suggested!.Code);
        Assert.NotEqual("TEMP_5", result.Suggested.Code);
    }

    [Theory]
    [InlineData(
        "laminado templado 5 mm + pvb 1.14 inc + 5 mm",
        "LAMT_5_114_5_INC")]
    [InlineData(
        "laminado crudo 4 mm + pvb 0.38 inc + 6 mm",
        "LAM_4_038_6_INC")]
    [InlineData(
        "laminado crudo 4 mm + pvb 1,14 inc + 6 mm",
        "LAM_4_114_6_INC")]
    [InlineData(
        "laminado crudo 4 mm + pvb 0.76 inc + 6 mm",
        "LAM_4_076_6_INC")]
    [InlineData(
        "laminado crudo 6 mm + pvb 0,76 acústico + 8 mm",
        "LAM_6_076_AC_8_INC")]
    [InlineData(
        "laminado templado 6 mm + pvb 1.52 inc + 6 mm",
        "LAMT_6_152_6_INC")]
    public void Resolve_LaminatedHistoricalBdGnCompositions_ReturnsExactCatalogGlass(
        string raw,
        string expectedCode)
    {
        var result = Resolver.Resolve(Input(raw: raw), ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(expectedCode, result.Suggested!.Code);
    }

    [Theory]
    [InlineData(
        "templado 5 mm inc + cámara 12 + templado 6 mm inc")]
    [InlineData(
        "templado 5 mm inc + cámara 12 mm + templado 6 mm inc")]
    public void Resolve_ChamberAliases_ReturnSameCanonicalCatalogGlass(
        string raw)
    {
        var result = Resolver.Resolve(Input(raw: raw), ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("IGU_T5_CAM12_T6", result.Suggested!.Code);
    }

    [Theory]
    [InlineData("CL167", "QG_PREMIUM_CL167")]
    [InlineData("CL120", "QG_PREMIUM_CL120")]
    [InlineData("CL150", "QG_PREMIUM_CL150")]
    [InlineData(
        "Quality Glass Classic Green laminado 4 mm",
        "QG_CLASSIC_GREEN")]
    [InlineData(
        "Quality Glass Classic Blue laminado 4 mm",
        "QG_CLASSIC_BLUE")]
    [InlineData(
        "Quality Glass Classic Bronze laminado 4 mm",
        "QG_CLASSIC_BRONZE")]
    public void Resolve_QualityGlassProductTokens_ReturnExactCatalogGlass(
        string raw,
        string expectedCode)
    {
        var result = Resolver.Resolve(Input(raw: raw), ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(expectedCode, result.Suggested!.Code);
    }

    [Fact]
    public void Resolve_GenericBlueWithoutQualityGlassContext_DoesNotSelectQualityGlass()
    {
        var result = Resolver.Resolve(
            Input(raw: "vidrio azul 4 mm"),
            ExpandedCatalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.DoesNotContain(
            result.Alternatives,
            value => value.Code == "QG_CLASSIC_BLUE");
    }

    [Fact]
    public void Resolve_NotApplicable_ReturnsExplicitCatalogOption()
    {
        var result = Resolver.Resolve(Input(raw: "N.A."), ExpandedCatalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("GLASS_NA", result.Suggested!.Code);
        Assert.NotEqual("UNKNOWN_GLASS", result.Suggested.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(50)]
    public void ResolveMany_ProcessesAnyItemCountWithoutFixedLimit(int count)
    {
        var inputs = Enumerable.Range(0, count)
            .Select(_ => Input(type: "templado", thickness: 6))
            .ToArray();

        var results = Resolver.ResolveMany(inputs, Catalog());

        Assert.Equal(count, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.RequiresReview);
            Assert.Equal("TEMP_6", result.Suggested!.Code);
        });
    }

    private static GlassCandidateResolutionInput Input(
        string? raw = null,
        string? type = null,
        decimal? thickness = null,
        string? color = null,
        string? composition = null) =>
        new(
            raw,
            type,
            type,
            thickness,
            color,
            color,
            null,
            null,
            composition,
            null,
            null);

    internal static IReadOnlyList<GlassTypeCatalogReadModel> Catalog() =>
    [
        Glass("TEMP_5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC"),
        Glass("TEMP_6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC"),
        Glass("TEMP_8", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC"),
        Glass("TEMP_10", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC"),
        Glass("LAM_4_4", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC"),
        Glass("LAM_4_4_GRAY", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM GRIS + 4 MM INC"),
        Glass("LAM_5_5", "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC"),
        Glass("LAM_5_5_GRAY", "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM GRIS + 5 MM INC"),
        Glass("UNKNOWN_GLASS", "Tipo de vidrio por confirmar")
    ];

    internal static IReadOnlyList<GlassTypeCatalogReadModel> ExpandedCatalog() =>
    [
        ..Catalog(),
        Glass("TEMP_4", "COMPOSICION MONOLITICO TEMPLADO 4 MM INC", family: "MONOLITHIC", composition: "TEMPERED", outerThicknessMm: 4m, color: "INC"),
        Glass("RAW_4_INC", "COMPOSICION MONOLITICO CRUDO 4 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 4m, color: "INC"),
        Glass("RAW_4_MINI_BOREAL", "COMPOSICION MONOLITICO CRUDO 4 MM MINI BOREAL", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 4m, pattern: "MINI_BOREAL"),
        Glass("RAW_5_INC", "COMPOSICION MONOLITICO CRUDO 5 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 5m, color: "INC"),
        Glass("RAW_6_INC", "COMPOSICION MONOLITICO CRUDO 6 MM INC", family: "MONOLITHIC", composition: "RAW", outerThicknessMm: 6m, color: "INC"),
        Glass("LAM_4_038_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 0.38m, pvbColor: "INC"),
        Glass("LAM_4_076_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,76 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 0.76m, pvbColor: "INC"),
        Glass("LAM_4_114_6_INC", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 1,14 MM INC + 6 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 6m, pvbThicknessMm: 1.14m, pvbColor: "INC"),
        Glass("LAM_6_076_AC_8_INC", "COMPOSICION LAMINADO CRUDO 6 MM INC + PVB 0,76 MM ACÚSTICO + 8 MM INC", family: "LAMINATED", composition: "RAW", outerThicknessMm: 6m, innerThicknessMm: 8m, pvbThicknessMm: 0.76m, pvbType: "ACOUSTIC", pvbColor: "INC"),
        Glass("LAMT_5_114_5_INC", "COMPOSICION LAMINADO TEMPLADO 5 MM INC + PVB 1,14 MM INC + 5 MM INC", family: "LAMINATED", composition: "TEMPERED", outerThicknessMm: 5m, innerThicknessMm: 5m, pvbThicknessMm: 1.14m, pvbColor: "INC"),
        Glass("LAMT_6_152_6_INC", "COMPOSICION LAMINADO TEMPLADO 6 MM INC + PVB 1,52 MM INC + 6 MM INC", family: "LAMINATED", composition: "TEMPERED", outerThicknessMm: 6m, innerThicknessMm: 6m, pvbThicknessMm: 1.52m, pvbColor: "INC"),
        Glass("IGU_T5_CAM12_T6", "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM + TEMPLADO 6 MM INC", family: "IGU", composition: "TEMPERED", outerThicknessMm: 5m, innerThicknessMm: 6m, chamberThicknessMm: 12m, color: "INC"),
        Glass("QG_PREMIUM_CL120", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL120", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL120"),
        Glass("QG_PREMIUM_CL150", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL150", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL150"),
        Glass("QG_PREMIUM_CL167", "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL167", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_PREMIUM", productToken: "CL167"),
        Glass("QG_CLASSIC_BLUE", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BLUE", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "BLUE", color: "BLUE"),
        Glass("QG_CLASSIC_BRONZE", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BRONZE", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "BRONZE", color: "BRONZE"),
        Glass("QG_CLASSIC_GREEN", "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM GREEN", family: "LAMINATED", composition: "RAW", outerThicknessMm: 4m, innerThicknessMm: 4m, pvbThicknessMm: 0.38m, pvbColor: "INC", productLine: "QUALITY_GLASS_CLASSIC", productToken: "GREEN", color: "GREEN"),
        Glass("GLASS_NA", "N.A.", family: "NOT_APPLICABLE", requiresReview: true)
    ];

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string name,
        string? family = null,
        string? composition = null,
        decimal? outerThicknessMm = null,
        decimal? innerThicknessMm = null,
        decimal? pvbThicknessMm = null,
        string? pvbType = null,
        string? pvbColor = null,
        decimal? chamberThicknessMm = null,
        string? productLine = null,
        string? productToken = null,
        string? pattern = null,
        string? color = null,
        bool requiresReview = false) =>
        new(
            Guid.NewGuid(),
            code,
            name,
            null,
            true,
            new GlassPriceRangeCatalogReadModel(
                Guid.NewGuid(),
                1,
                1m,
                1m,
                1m,
                "COP",
                GlassPriceRangeStatus.Preliminary,
                At,
                null),
            family,
            composition,
            null,
            outerThicknessMm,
            innerThicknessMm,
            pvbThicknessMm,
            pvbType,
            pvbColor,
            chamberThicknessMm,
            productLine,
            productToken,
            pattern,
            color,
            true,
            requiresReview);
}
