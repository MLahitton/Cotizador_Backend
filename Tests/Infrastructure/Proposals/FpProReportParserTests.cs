using System.Text;
using Application.Proposals.FpPro;
using Application.Common.Abstractions.Proposals;
using Infrastructure.Proposals.FpPro;
using Xunit;


namespace CotizadorBackend.Tests.Infrastructure.Proposals;

public sealed class FpProReportParserTests
{
    private const string PdfContentType = "application/pdf";

    [Theory]
    [InlineData("629.905,1", "629905.1")]
    [InlineData("18,967", "18.967")]
    [InlineData("14,1350", "14.1350")]
    public void ParseLatinDecimal_UsesFpProNumberFormat(
        string input,
        string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            FpProReportParser.ParseLatinDecimal(input));
    }

    [Fact]
    public void CalculateRawAccessoriesBase_IncludesAccessoryMlSection()
    {
        const string detailText = """
            Accesorios Marca Cantidad Valor Total
            CIERRE 1,0 10.000,0 10.000,0
            Acc. ml Marca Cantidad Valor Total
            CALZA VIDRIO 24X10MM 0,2 8.021,0 1.604,2
            Guarniciones Marca Cantidad Valor Total
            FELPA 2,0 3.000,0 6.000,0
            Vidrios Codigo Cantidad Valor Total
            """;

        var result = FpProReportParser.CalculateRawAccessoriesBase(detailText);

        Assert.Equal(17604.2m, result);
    }

    [Fact]
    public void CalculateRawAccessoriesBase_IncludesAccessoryMlWithoutDot()
    {
        const string detailText = """
            Accesorios Marca Cantidad Valor Total
            TOPE 1,0 100,0 100,0
            Acc ml Marca Cantidad Valor Total
            CALZA VIDRIO 24X10MM 0,4 8.021,0 3.208,4
            Guarniciones Marca Cantidad Valor Total
            EMPAQUE 1,0 20,0 20,0
            Vidrios Codigo Cantidad Valor Total
            """;

        var result = FpProReportParser.CalculateRawAccessoriesBase(detailText);

        Assert.Equal(3328.4m, result);
    }

    [Fact]
    public void CalculateRawAccessoriesBase_IncludesAccessoryMlWithFpProRealHeader()
    {
        const string detailText = """
            Accesorios MarcaArt-NrDescripcionTratt.Sup.Cant.Precio $Var.%Prezzototale $
            S&G0001ACCESORIO BASE EXTRUSI1,025.497,90 $25.497,9
            Acc. ml MarcaArt-NrDescripcionTratt.Sup.Cant.Precio $Var.%Prezzototale $
            ALUMINA9448CALZA VIDRIO 24X10MM REF C210258 EXTRUSI0,28.021,00 $1.604,2
            1.604,2
            Guarniciones Fecha: 19/09/2026S&G1035 - Revision 2Pagina 10PedidoS&G1035
            MarcaArt-NrDescripcionTratt.Sup.mPrecio $Var.%Prezzototale $
            ALUMINA100639EMPAQUE CUNA FIJO PISAVIDRIO GRUESO CK77,12.520,00 $17.924,8
            ALUMINA17188EMPAQUE CUNA MOVIL C041092 5MM EXTRUSI7,12.074,00 $14.752,3
            32.677,1
            Vidrios Codigo Cantidad Valor Total
            """;

        var result = FpProReportParser.CalculateRawAccessoriesBase(detailText);

        Assert.Equal(59779.2m, result);
    }

    [Fact]
    public void CalculateRawAccessoriesBase_IncludesAccessoryMlWithFpProRealHeaderForDoubleQuantity()
    {
        const string detailText = """
            Accesorios MarcaArt-NrDescripcionTratt.Sup.Cant.Precio $Var.%Prezzototale $
            S&G0001ACCESORIO BASE EXTRUSI1,025.057,90 $25.057,9
            Acc. ml MarcaArt-NrDescripcionTratt.Sup.Cant.Precio $Var.%Prezzototale $
            ALUMINA9448CALZA VIDRIO 24X10MM REF C210258 EXTRUSI0,48.021,00 $3.208,4
            3.208,4
            Guarniciones Fecha: 19/09/2026S&G1035 - Revision 2Pagina 46PedidoS&G1035
            MarcaArt-NrDescripcionTratt.Sup.mPrecio $Var.%Prezzototale $
            ALUMINA100639EMPAQUE CUNA FIJO PISAVIDRIO GRUESO CK76,62.520,00 $16.538,8
            ALUMINA17188EMPAQUE CUNA MOVIL C041092 5MM EXTRUSI6,62.062,36 $13.611,6
            30.150,4
            Vidrios Codigo Cantidad Valor Total
            """;

        var result = FpProReportParser.CalculateRawAccessoriesBase(detailText);

        Assert.Equal(58416.7m, result);
    }
    [Fact]
    public void CalculateRawAccessoriesBase_AllowsMissingAccessoryMlSection()
    {
        const string detailText = """
            Accesorios Marca Cantidad Valor Total
            CIERRE 1,0 5.000,5 5.000,5
            Guarniciones Marca Cantidad Valor Total
            EMPAQUE 1,0 2.500,25 2.500,25
            Vidrios Codigo Cantidad Valor Total
            """;

        var result = FpProReportParser.CalculateRawAccessoriesBase(detailText);

        Assert.Equal(7500.75m, result);
    }

    [Fact]
    public void ParseDimension_ExtractsMillimeters()
    {
        var result = FpProReportParser.ParseDimension("4550x3200");

        Assert.NotNull(result);
        Assert.Equal(4550, result!.Value.Width);
        Assert.Equal(3200, result.Value.Height);
    }

    [Fact]
    public void ParseGlass_ExtractsSinglePane()
    {
        var result = FpProReportParser.ParseGlass("10MM (4449 x 3094) x 1");

        var glass = Assert.Single(result);
        Assert.Equal("10MM", glass.Code);
        Assert.Equal(10m, glass.ThicknessMm);
        Assert.Equal(4449, glass.WidthMm);
        Assert.Equal(3094, glass.HeightMm);
        Assert.Equal(1, glass.Quantity);
    }

    [Fact]
    public void ParseGlass_ExtractsMultiplePanesAndAllowsMaxThicknessRule()
    {
        var result = FpProReportParser.ParseGlass("05MM (662 x 1644) x 4 06MM (1430 x 805) x 2");

        Assert.Equal(2, result.Count);
        Assert.Equal(6m, result.Max(value => value.ThicknessMm));
    }

    [Fact]
    public async Task ParseAsync_WithInvalidPdf_ThrowsInvalidDataException()
    {
        var parser = new FpProReportParser();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

        await Assert.ThrowsAsync<InvalidDataException>(() => parser.ParseAsync(
            new FpProReportFile("bad.pdf", PdfContentType, stream.Length, stream),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("S&G648_t2.PDF")]
    [InlineData("S&G1049.PDF")]
    [InlineData("S&G1043.PDF")]
    public async Task Debug_ProfileBarCount(string fileName)
    {
        var preview = await ParseFixtureAsync(fileName);

        Console.WriteLine($"{fileName} -> ProfileBarCount: {preview.Report.ProfileBarCount}");
    }

    [Fact]
    public async Task ParseAsync_CasaPsFixture_ExtractsPreviewItemsAndItem01()
    {
        var preview = await ParseFixtureAsync("S&G648_t2.PDF");

        Assert.Equal("S&G648", preview.Report.OrderId);
        Assert.Equal(22m, preview.Report.AluminumWastePercent);
        Assert.Equal(238, preview.Report.ProfileBarCount);
        Assert.Equal(28, preview.Report.ItemsDetected);
        Assert.Equal(30, preview.Report.StructureCount);
        var item01 = preview.Items.Single(item => item.ItemNumber == "01");
        Assert.Equal("V-1", item01.Typology);
        Assert.Equal(["KONCEPT40", "ALFAJIA"], item01.FpProProfiles);
        Assert.Equal(4550, item01.WidthMm);
        Assert.Equal(3200, item01.HeightMm);
        Assert.Equal(4.55m, item01.WidthM);
        Assert.Equal(3.2m, item01.HeightM);
        Assert.Equal(1, item01.Quantity);
        Assert.Equal(14.56m, item01.NominalAreaM2);
        var item01Glass = Assert.Single(item01.Glass);
        Assert.Equal("10MM", item01Glass.Code);
        Assert.Equal(10m, item01Glass.ThicknessMm);
        Assert.Equal(4449, item01Glass.WidthMm);
        Assert.Equal(3094, item01Glass.HeightMm);
        Assert.Equal(1, item01Glass.Quantity);
        Assert.Equal(10m, item01.SelectedThicknessMm);
        Assert.Equal(629905.1m, item01.AluminumBase);
        Assert.Equal(101352.1m, item01.AccessoriesBase);
        Assert.Equal(18.967m, item01.StructureWeightKg);
        Assert.Equal(18.967m, item01.StructureWeightKgUnit);
        Assert.Equal("INCLUYE MARCO SG0058, INCLUYE CRISTAL A JUNTA DIVIDIDO EN CUATRO SECCIONES, INCLUYE ALFAJÍA DE 114MM", item01.Notes);
        Assert.NotNull(item01.Image);
        Assert.Equal("image/png", item01.Image!.ContentType);
        Assert.True(Convert.FromBase64String(item01.Image.Base64).Length > 0);
        var item02 = preview.Items.Single(item => item.ItemNumber == "02");
        Assert.NotNull(item02.Image);
        Assert.NotEqual(item01.Image.Base64, item02.Image!.Base64);
        var item06 = preview.Items.Single(item => item.ItemNumber == "06");
        Assert.Equal(2, item06.Quantity);
        Assert.Equal(1500, item06.WidthMm);
        Assert.Equal(2700, item06.HeightMm);
        Assert.Equal(8.10m, item06.NominalAreaM2);
        Assert.Equal(15.613m, item06.StructureWeightKg);
        Assert.Equal(15.613m, item06.StructureWeightKgUnit);
        Assert.Equal(2, item06.Glass.Count);
        Assert.Contains(item06.Glass, glass => glass.Code == "05MM");
        Assert.Contains(item06.Glass, glass => glass.Code == "06MM");
        Assert.Equal(6m, item06.SelectedThicknessMm);
        var item08 = preview.Items.Single(item => item.ItemNumber == "08");
        Assert.Equal(16.605m, item08.StructureWeightKg);
        Assert.Equal(16.605m, item08.StructureWeightKgUnit);
        var item15 = preview.Items.Single(item => item.ItemNumber == "15");
        Assert.Equal(15.495m, item15.StructureWeightKg);
        Assert.Equal(15.495m, item15.StructureWeightKgUnit);
        var item17 = preview.Items.Single(item => item.ItemNumber == "17");
        Assert.NotNull(item17.StructureWeightKg);
        Assert.True(item17.StructureWeightKg > 1m);
        Assert.All(preview.Items, item => Assert.NotEqual(1m, item.StructureWeightKg));
        var item24 = preview.Items.Single(item => item.ItemNumber == "24");
        Assert.Equal(2, item24.Quantity);
        Assert.Equal(3200, item24.WidthMm);
        Assert.Equal(2700, item24.HeightMm);
        Assert.Equal(17.28m, item24.NominalAreaM2);
        Assert.Equal(item24.AluminumBase, item24.AluminumBaseUnit);
        Assert.Equal(item24.StructureWeightKg, item24.StructureWeightKgUnit);
        Assert.Contains("system", item01.PendingFields);
        Assert.Contains("glassDescription", item01.PendingFields);
        Assert.Contains("finish", item01.PendingFields);
        Assert.Contains("glassPrice", item01.PendingFields);
    }

    [Fact]
    public async Task ResolveAsync_CasaPsFixture_ResolvesRepresentativeSystems()
    {
        var preview = await ResolveFixtureAsync("S&G648_t2.PDF");

        var fixedFermo = preview.Items.First(item =>
            item.FpProProfiles.SequenceEqual(["KONCEPT40", "ALFAJIA"]));
        Assert.Equal("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", fixedFermo.System?.Trim());
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", fixedFermo.GlassDescription?.Trim());
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", fixedFermo.Finish?.Trim());
        Assert.Equal("N.A", fixedFermo.Lock);
        Assert.Equal(["module"], fixedFermo.PendingFields);

        var napoles = preview.Items.First(item =>
            item.FpProProfiles.SequenceEqual(["KONCEPT70", "ANGULOS"])
            && !ContainsIgnoreCaseAndAccents(item.Notes, "BOLSILLO"));
        Assert.Equal("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES", napoles.System?.Trim());
        Assert.Equal("CERRADURA MULTI-PUNTO MANIJA ITALIANA", napoles.Lock);

        var pocket = preview.Items.First(item =>
            item.FpProProfiles.SequenceEqual(["KONCEPT70", "ANGULOS"])
            && ContainsIgnoreCaseAndAccents(item.Notes, "BOLSILLO"));
        Assert.Equal("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET", pocket.System);
        Assert.Equal("CERRADURA MULTI-PUNTO MANIJA ITALIANA", pocket.Lock);
    }

    [Fact]
    public async Task ParseAsync_Sg1043Fixture_ExtractsAllItemsAndItem01()
    {
        var preview = await ParseFixtureAsync("S&G1043.PDF");

        Assert.Equal(20m, preview.Report.AluminumWastePercent);
        Assert.Equal(147, preview.Report.ProfileBarCount);
        Assert.Equal(13, preview.Report.ItemsDetected);
        Assert.Equal(17, preview.Report.StructureCount);
        var item01 = preview.Items.Single(item => item.ItemNumber == "01");
        Assert.Equal("V1", item01.Typology);
        Assert.Equal(["SERIE35", "ALFAJIA"], item01.FpProProfiles);
        Assert.Equal(750, item01.WidthMm);
        Assert.Equal(2250, item01.HeightMm);
        Assert.Equal(0.75m, item01.WidthM);
        Assert.Equal(2.25m, item01.HeightM);
        Assert.Equal(2, item01.Quantity);
        Assert.Equal(3.3750m, item01.NominalAreaM2);
        Assert.Equal(261340.25m, item01.AluminumBase);
        Assert.Equal(191708.7m, item01.AccessoriesBase);
        Assert.Equal(7.2125m, item01.StructureWeightKg);
        Assert.Equal(7.2125m, item01.StructureWeightKgUnit);
        Assert.Equal(2, item01.Glass.Count);
        Assert.Contains(item01.Glass, glass => glass.Code == "05MM" && glass.WidthMm == 638 && glass.HeightMm == 638 && glass.Quantity == 2);
        Assert.Contains(item01.Glass, glass => glass.Code == "05MM" && glass.WidthMm == 702 && glass.HeightMm == 700 && glass.Quantity == 4);
        Assert.NotNull(item01.Image);
        Assert.True(Convert.FromBase64String(item01.Image!.Base64).Length > 0);
    }

    [Fact]
    public async Task ResolveAsync_Sg1043Fixture_ResolvesSerie35WithTechnicalDescriptions()
    {
        var preview = await ResolveFixtureAsync("S&G1043.PDF");

        var projecting = preview.Items.FirstOrDefault(item =>
            item.System == "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA");
        var casement = preview.Items.FirstOrDefault(item =>
            item.System == "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA");

        Assert.NotNull(projecting);
        Assert.Contains(projecting!.TechnicalProfileDescriptions, value =>
            value.Contains("PROYECTANTE", StringComparison.OrdinalIgnoreCase)
            || value.Contains("NAVE HORIZ/VERT", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(casement);
        Assert.Contains(casement!.TechnicalProfileDescriptions, value =>
            value.Contains("BATIENTE", StringComparison.OrdinalIgnoreCase)
            || value.Contains("NAVE2295", StringComparison.OrdinalIgnoreCase)
            || value.Contains("NAVE CAMARA EUROPEA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseAsync_Sg1049Fixture_ExtractsAllItemsAndItem01()
    {
        var preview = await ParseFixtureAsync("S&G1049.PDF");

        Assert.Equal(15m, preview.Report.AluminumWastePercent);
        Assert.Equal(242, preview.Report.ProfileBarCount);
        Assert.Equal(40, preview.Report.ItemsDetected);
        Assert.Equal(44, preview.Report.StructureCount);
        var item01 = preview.Items.Single(item => item.ItemNumber == "01");
        Assert.Equal("V-1", item01.Typology);
        Assert.Equal(["SUPERIOR50", "ALFAJIA"], item01.FpProProfiles);
        Assert.Equal(3000, item01.WidthMm);
        Assert.Equal(800, item01.HeightMm);
        Assert.Equal(3.0m, item01.WidthM);
        Assert.Equal(0.8m, item01.HeightM);
        Assert.Equal(1, item01.Quantity);
        Assert.Equal(2.4m, item01.NominalAreaM2);
        Assert.Equal(256932.9m, item01.AluminumBase);
        Assert.Equal(95677.8m, item01.AccessoriesBase);
        Assert.Equal(7.193m, item01.StructureWeightKg);
        Assert.Equal(7.193m, item01.StructureWeightKgUnit);
        Assert.Equal(2, item01.Glass.Count);
        Assert.Contains(item01.Glass, glass => glass.Code == "05MM" && glass.WidthMm == 1445 && glass.HeightMm == 706 && glass.Quantity == 1);
        Assert.Contains(item01.Glass, glass => glass.Code == "05MM" && glass.WidthMm == 1467 && glass.HeightMm == 706 && glass.Quantity == 1);
        Assert.NotNull(item01.Image);
        Assert.True(Convert.FromBase64String(item01.Image!.Base64).Length > 0);
    }

    [Fact]
    public async Task ResolveAsync_Sg1049Fixture_ResolvesItem01AndItem03()
    {
        var preview = await ResolveFixtureAsync("S&G1049.PDF");

        var item01 = preview.Items.Single(item => item.ItemNumber == "01");
        Assert.Equal("VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO", item01.System?.Trim());
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 5 MM INC", item01.GlassDescription?.Trim());
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", item01.Finish?.Trim());
        Assert.Equal("CIERRE EMBUTIDO DE IMPACTO AUTOMATICO", item01.Lock);
        Assert.Equal(["module"], item01.PendingFields);
        var item03 = preview.Items.Single(item => item.ItemNumber == "03");
        Assert.Equal(["KONCEPT50", "ALFAJIA"], item03.FpProProfiles);
        Assert.Equal("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", item03.System?.Trim());
        Assert.Equal("CIERRE EMBUTIDO DE IMPACTO AUTOMATICO", item03.Lock);
    }

    [Fact]
    public async Task ParseAsync_Sg1085Fixture_DetectsStructureCount()
    {
        var preview = await ParseFixtureAsync("S&G1085.PDF");

        Assert.Equal(15, preview.Report.ItemsDetected);
        Assert.Equal(19, preview.Report.StructureCount);
    }

    [Fact]
public async Task ParseAsync_Sg1085Fixture_DetectsTemperedGlassTreatment()
{
    var parser = new FpProReportParser();

    await using var stream = File.OpenRead(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "FpPro",
        "S&G1085.PDF"));

    var result = await parser.ParseAsync(
        new FpProReportFile(
            "S&G1085.PDF",
            "application/pdf",
            stream.Length,
            stream),
        TestContext.Current.CancellationToken);

    var item01 = Assert.Single(result.Items, item => item.ItemNumber == "01");
    Assert.Equal(2, item01.Glass.Count);

    Assert.All(item01.Glass, glass =>
    {
        Assert.Equal("06MM", glass.Code);
        Assert.Equal("TEMPLADO", glass.Treatment);
    });
    }

    private static async Task<FpProReportPreviewData> ParseFixtureAsync(string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "FpPro",
            fileName);
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "Fixtures",
                "FpPro",
                fileName));
        }

        await using var stream = File.OpenRead(path);
        var parser = new FpProReportParser();
        return await parser.ParseAsync(
            new FpProReportFile(fileName, PdfContentType, stream.Length, stream),
            TestContext.Current.CancellationToken);
    }

    private static async Task<FpProReportPreviewData> ResolveFixtureAsync(string fileName)
    {
        var preview = await ParseFixtureAsync(fileName);
        var resolver = new FpProPreviewConfigurationResolver(new QuotationTemplateCatalogReader());
        return await resolver.ResolveAsync(preview, TestContext.Current.CancellationToken);
    }

    private static bool ContainsIgnoreCaseAndAccents(string? value, string expected)
    {
        static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        return Normalize(value).Contains(Normalize(expected), StringComparison.Ordinal);
    }
}
