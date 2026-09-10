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
    public async Task ResolveAsync_WithResolvedValues_LeavesOnlyGlassPricePending()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            Preview(Item(["KONCEPT50", "ALFAJIA"], selectedThicknessMm: 5m)),
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", item.System);
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 5 MM INC", item.GlassDescription);
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", item.Finish);
        Assert.Equal("CIERRE EMBUTIDO DE IMPACTO AUTOMATICO", item.Lock);
        Assert.Null(item.GlassPrice);
        Assert.Equal(["glassPrice", "module"], item.PendingFields);
        Assert.Equal(["glassPrice", "module"], result.PendingFields);
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
        new(new FpProReportData("S&G", "Fixture", 1, 1, 20m), [item], item.PendingFields);

    private static FpProPreviewItemData Item(
        IReadOnlyList<string> profiles,
        decimal? selectedThicknessMm = 5m,
        string? notes = null,
        IReadOnlyList<string>? technicalDescriptions = null) =>
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
            Glass: [],
            SelectedThicknessMm: selectedThicknessMm,
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
