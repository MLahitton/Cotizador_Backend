using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Proposals;

public sealed class FpProPreviewConfigurationResolverTests
{
    [Theory]
    [InlineData("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "KONCEPT40", "ALFAJIA")]
    [InlineData("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "KONCEPT40", "ALFAJIA", "VITRINA")]
    [InlineData("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", "KONCEPT50", "ALFAJIA")]
    [InlineData("VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO", "SUPERIOR50", "ALFAJIA")]
    [InlineData("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO", "KONCEPT100", "ANGULOS")]
    [InlineData("PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", "3890", "SERIE35", "VITRINA")]
    [InlineData("SISTEMA SG CLARABOYA", "TUBULARES")]
    public async Task ResolveAsync_WithKnownProfiles_ResolvesSystem(
        string expectedSystem,
        params string[] profiles)
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(profiles)),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(expectedSystem, item.System);
        Assert.DoesNotContain("system", item.PendingFields);
    }

    [Theory]
    [InlineData(null, "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES")]
    [InlineData("INCLUYE BOLSILLO INFERIOR", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET")]
    public async Task ResolveAsync_WithKoncept70_UsesPocketOnlyWhenNotesContainBolsillo(
        string? notes,
        string expectedSystem)
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(["KONCEPT70", "ANGULOS"], notes: notes)),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedSystem, Assert.Single(result.Items).System);
    }

    [Theory]
    [InlineData("MARCO VENTANA PROYECTANTE", "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA")]
    [InlineData("PISAVIDRIO PUERTA BATIENTE", "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA")]
    public async Task ResolveAsync_WithSerie35TechnicalIndicators_DiscriminatesSystem(
        string technicalDescription,
        string expectedSystem)
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(["SERIE35", "ALFAJIA"], technicalDescriptions: [technicalDescription])),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedSystem, Assert.Single(result.Items).System);
    }

    [Fact]
    public async Task ResolveAsync_WithAmbiguousSerie35_LeavesSystemAndLockPending()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(["SERIE35", "ALFAJIA"])),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Null(item.System);
        Assert.Null(item.Lock);
        Assert.Contains("system", item.PendingFields);
        Assert.Contains("lock", item.PendingFields);
    }

    [Theory]
    [InlineData("5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC")]
    [InlineData("6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC")]
    [InlineData("8", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC")]
    [InlineData("10", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC")]
    public async Task ResolveAsync_WithKnownThickness_ResolvesGlassDescription(
        string thickness,
        string expectedGlassDescription)
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(["KONCEPT50", "ALFAJIA"], selectedThicknessMm: decimal.Parse(thickness))),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(expectedGlassDescription, item.GlassDescription);
        Assert.DoesNotContain("glassDescription", item.PendingFields);
    }

    [Fact]
    public async Task ResolveAsync_WithResolvedValuesAndSafeGlassPrice_LeavesOnlyModulePending()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(
                ["KONCEPT50", "ALFAJIA"],
                selectedThicknessMm: 5m,
                glass:
                [
                    new FpProGlassPaneData("05MM", null, 5m, 1000, 1000, 1)
                ])),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", item.System);
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 5 MM INC", item.GlassDescription);
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", item.Finish);
        Assert.Equal("CIERRE EMBUTIDO DE IMPACTO AUTOMATICO", item.Lock);
        Assert.Equal(74000m, item.GlassPrice);
        Assert.Equal(["module"], item.PendingFields);
        Assert.Equal(["module"], result.PendingFields);
    }

        [Fact]
    public async Task ResolveAsync_WithUnknownGlassCode_KeepsGlassPricePending()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(
                ["KONCEPT50", "ALFAJIA"],
                selectedThicknessMm: 5m,
                glass:
                [
                    new FpProGlassPaneData("LAMINADO",null, null, 1000, 1000, 1)
                ])),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Null(item.GlassPrice);
        Assert.Contains("glassPrice", item.PendingFields);
        Assert.Contains("glassPrice", result.PendingFields);
    }

    [Theory]
[InlineData("05MM", 5, 74000)]
[InlineData("06MM", 6, 74000)]
[InlineData("08MM", 8, 90000)]
[InlineData("10MM", 10, 126000)]
public async Task ResolveAsync_WithKnownGlassCode_CalculatesSafeGlassPrice(
    string code,
    decimal thickness,
    decimal expectedGlassPrice)
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT50", "ALFAJIA"],
            selectedThicknessMm: thickness,
            glass:
            [
                new FpProGlassPaneData(code, null, thickness, 1000, 1000, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);
    Assert.Equal(expectedGlassPrice, item.GlassPrice);
    Assert.DoesNotContain("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithTemperedSixMillimeterGlass_CalculatesTemperedGlassPrice()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT70"],
            selectedThicknessMm: 6m,
            glass:
            [
                new FpProGlassPaneData("06MM", "TEMPLADO", 6m, 1665, 2333, 2),
                new FpProGlassPaneData("06MM", "TEMPLADO", 6m, 1665, 2334, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);

    Assert.Equal(1002330m, item.GlassPrice);
    Assert.DoesNotContain("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithSpecialGlassModifier_KeepsGlassPricePending()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["TUBULARES"],
            selectedThicknessMm: 6m,
            notes: "INCLUYE OFF SIDE DE 15CM PERIMETRAL",
            glass:
            [
                new FpProGlassPaneData("06MM", "TEMPLADO", 6m, 1653, 844, 3)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);

    Assert.Null(item.GlassPrice);
    Assert.Contains("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithSingleKnownGlassBelowOneSquareMeter_AppliesOneSquareMeterMinimum()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT50", "ALFAJIA"],
            selectedThicknessMm: 5m,
            glass:
            [
                new FpProGlassPaneData("05MM", null, 5m, 500, 500, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);
    Assert.Equal(74000m, item.GlassPrice);
    Assert.DoesNotContain("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithSingleKnownGlassAboveOneSquareMeter_UsesMeasuredArea()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT50", "ALFAJIA"],
            selectedThicknessMm: 6m,
            glass:
            [
                new FpProGlassPaneData("06MM", null, 6m, 1890, 1224, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);
    Assert.Equal(171188.6m, item.GlassPrice);
    Assert.DoesNotContain("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithMixedKnownGlassAboveOneSquareMeterPerClass_CalculatesCombinedPrice()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT50", "ALFAJIA"],
            selectedThicknessMm: 8m,
            glass:
            [
                new FpProGlassPaneData("05MM", null, 5m, 2000, 1000, 1),
                new FpProGlassPaneData("08MM", null, 8m, 3000, 1000, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);
    Assert.Equal(418000m, item.GlassPrice);
    Assert.DoesNotContain("glassPrice", item.PendingFields);
}

[Fact]
public async Task ResolveAsync_WithMixedKnownGlassBelowOneSquareMeterForAnyClass_KeepsGlassPricePending()
{
    var resolver = CreateResolver();

    var result = await resolver.ResolveAsync(
        Preview(Item(
            ["KONCEPT50", "ALFAJIA"],
            selectedThicknessMm: 8m,
            glass:
            [
                new FpProGlassPaneData("05MM", null, 5m, 500, 500, 1),
                new FpProGlassPaneData("08MM", null, 8m, 3000, 1000, 1)
            ])),
        TestContext.Current.CancellationToken);

    var item = Assert.Single(result.Items);
    Assert.Null(item.GlassPrice);
    Assert.Contains("glassPrice", item.PendingFields);
    Assert.Contains("glassPrice", result.PendingFields);
}

    private static FpProPreviewConfigurationResolver CreateResolver()
    {
        var reader = Substitute.For<IQuotationTemplateCatalogReader>();
        reader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new QuotationTemplateCatalog(
                [
                    new("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "N.A"),
                    new("CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40", "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", "MANIJA CREMONA ITALIANA"),
                    new("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", "CIERRE EMBUTIDO DE IMPACTO AUTOMATICO"),
                    new("VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5", "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO", "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO", "CIERRE EMBUTIDO DE IMPACTO AUTOMATICO"),
                    new("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES", "CERRADURA MULTI-PUNTO MANIJA ITALIANA"),
                    new("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET", "CERRADURA MULTI-PUNTO MANIJA ITALIANA"),
                    new("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 100", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO", "CERRADURA MULTI-PUNTO MANIJA ITALIANA"),
                    new("PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890", "MANIJA LUJO BASIC"),
                    new("SISTEMA SG CLARABOYA", "SISTEMA SG CLARABOYA", "SISTEMA SG CLARABOYA", "N.A"),
                    new("CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA", "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA", "MANIJA LUJO BASIC"),
                    new("CUERPO BATIENTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4", "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA", "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA", "MANIJA LUJO BASIC")
                ],
                [
                    new("COMPOSICION MONOLITICO TEMPLADO 5 MM INC", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC"),
                    new("COMPOSICION MONOLITICO TEMPLADO 6 MM INC", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC"),
                    new("COMPOSICION MONOLITICO TEMPLADO 8 MM INC", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC"),
                    new("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC")
                ],
                [new("ALUCOLOR POLIESTER NEGRO MATE PP13", "ALUCOLOR POLIESTER NEGRO MATE PP13")],
                [new("BGA", "BGA")]));

        return new FpProPreviewConfigurationResolver(reader);
    }

    private static FpProReportPreviewData Preview(FpProPreviewItemData item) =>
        new(new FpProReportData("S&G", "Fixture", 1, 1, 1, 20m, null, null), [item], item.PendingFields);

    private static FpProPreviewItemData Item(
    IReadOnlyList<string> profiles,
    decimal? selectedThicknessMm = 5m,
    string? notes = null,
    IReadOnlyList<string>? technicalDescriptions = null,
    IReadOnlyList<FpProGlassPaneData>? glass = null) =>
        new(
            ItemNumber: "01",
            Typology: "V-1",
            FpProProfiles: profiles,
            TechnicalProfileDescriptions: technicalDescriptions ?? [],
            TechnicalProfiles: [],
            WidthMm: 1000,
            HeightMm: 1000,
            WidthM: 1m,
            HeightM: 1m,
            Quantity: 1,
            NominalAreaM2: 1m,
            Notes: notes,
            Glass: glass ?? [],            SelectedThicknessMm: selectedThicknessMm,
            System: null,
            GlassDescription: null,
            Finish: null,
            Lock: null,
            GlassPrice: null,
            AluminumBase: null,
            AluminumBaseUnit: null,
            AccessoriesBase: null,
            AccessoriesBaseUnit: null,
            StructureWeightKg: null,
            StructureWeightKgUnit: null,
            Image: null,
            PendingFields: ["system", "glassDescription", "finish", "lock", "glassPrice", "module"],
            Warnings: []);
}
