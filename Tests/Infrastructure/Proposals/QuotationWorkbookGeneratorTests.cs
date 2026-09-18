using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Xml.Linq;
using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using Infrastructure.Proposals.FpPro;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Proposals;

public sealed class QuotationWorkbookGeneratorTests
{
    private const string PdfContentType = "application/pdf";
    private const string FixtureSystem = "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO";
    private const string FixtureGlass = "COMPOSICION MONOLITICO TEMPLADO 10 MM INC";
    private const string FixtureFinish = "ALUCOLOR POLIESTER NEGRO MATE PP13";
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace MainDrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public async Task GenerateAsync_WithSg648Item01_WritesInputsAndPreservesTemplate()
    {
        var templatePath = TemplatePath();
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(
            templatePath,
            TestContext.Current.CancellationToken));
        var request = await CreateSg648Item01RequestAsync();
        var debugPngPath = Path.Combine(Path.GetTempPath(), "fp-pro-debug-item-01.png");
        await File.WriteAllBytesAsync(
            debugPngPath,
            Convert.FromBase64String(request.Items[0].ImageBase64),
            TestContext.Current.CancellationToken);
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        var outputPath = Path.Combine(Path.GetTempPath(), "S&G648_Item01_Fase2B.xlsx");
        await File.WriteAllBytesAsync(outputPath, workbook.Content, TestContext.Current.CancellationToken);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook.ContentType);
        Assert.Equal("S&G648.xlsx", workbook.FileName);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(
            templatePath,
            TestContext.Current.CancellationToken)));

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var sheets = ReadSheetNames(archive);
        Assert.Contains(sheets, value => value.StartsWith("COTIZ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("BD GN", sheets);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));

        Assert.Equal("01", ReadCell(archive, worksheet, "A15"));
        Assert.Equal("Cliente S&G", ReadCell(archive, worksheet, "C10"));
        Assert.Equal("CASA PS", ReadCell(archive, worksheet, "C11"));
        Assert.Equal("BGA", ReadCell(archive, worksheet, "C12"));
        Assert.Equal("SG ESENCIAL", ReadCell(archive, worksheet, "I10"));
        Assert.Equal("LUISA HERNANDEZ", ReadCell(archive, worksheet, "I12"));
        Assert.Equal("S&G648", ReadCell(archive, worksheet, "O10"));
        Assert.Equal("V-1", ReadCell(archive, worksheet, "B15"));
        Assert.Equal("CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO", ReadCell(archive, worksheet, "D15"));
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 10 MM INC", ReadCell(archive, worksheet, "D16").Trim());
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", ReadCell(archive, worksheet, "D17"));
        Assert.Equal("4.55", ReadCell(archive, worksheet, "J15"));
        Assert.Equal("3.2", ReadCell(archive, worksheet, "K15"));
        Assert.Equal("1", ReadCell(archive, worksheet, "M15"));
        Assert.Equal("0.22", ReadCell(archive, worksheet, "T13"));
        Assert.Equal("101352.1", ReadCell(archive, worksheet, "Q16"));
        Assert.Equal("629905.1", ReadCell(archive, worksheet, "R16"));
        Assert.Equal("100000", ReadCell(archive, worksheet, "S16"));
        Assert.Equal("0.6", ReadCell(archive, worksheet, "AG13"));
        Assert.Equal("0", ReadCell(archive, worksheet, "AI13"));
        Assert.Equal("3", ReadCell(archive, worksheet, "BE15"));
        Assert.Equal("10", ReadCell(archive, worksheet, "BJ15"));
        Assert.Equal("18.967", ReadCell(archive, worksheet, "BK15"));
        Assert.Contains("INCLUYE MARCO SG0058", ReadCell(archive, worksheet, "D20"));

        Assert.Equal("J15*K15*M15", ReadFormula(worksheet, "L15"));
        Assert.Equal("IFERROR(AL15,\"\")", ReadFormula(worksheet, "N15"));
        Assert.Equal("IFERROR(N15*M15,\"\")", ReadFormula(worksheet, "O15"));
        Assert.Equal("J15*K15*2.5*BJ15", ReadFormula(worksheet, "BL15"));
        Assert.Equal("BK15+BL15", ReadFormula(worksheet, "BM15"));
        Assert.Equal("364", ReadCell(archive, worksheet, "BL15"));
        Assert.Equal("382.967", ReadCell(archive, worksheet, "BM15"));
        AssertFormulaHasCachedValue(worksheet, "S20");
        AssertFormulaHasCachedValue(worksheet, "BN15");
        Assert.NotEmpty(ReadCell(archive, worksheet, "S20"));
        Assert.NotEmpty(ReadCell(archive, worksheet, "BN15"));
        Assert.Equal("IFERROR(VLOOKUP(D15,'BD GN'!C:E,3,),\"\")", ReadFormula(worksheet, "D19"));
        using (var templateArchive = new ZipArchive(File.OpenRead(templatePath), ZipArchiveMode.Read))
        {
            var templateWorksheet = ReadXml(templateArchive, ResolveWorksheetEntryName(templateArchive, QuotationSheetName(templateArchive)));
            AssertFormulaPreservedWithCachedValue(templateWorksheet, worksheet, "S20");
            AssertFormulaPreservedWithCachedValue(templateWorksheet, worksheet, "BN15");
            AssertBlockFormulaCellsReadyForRecalculation(templateWorksheet, worksheet, 15);
        }

        Assert.Equal(
            "COMPOSICION MONOLITICO TEMPLADO 10 MM INC ",
            ReadCell(archive, ReadXml(archive, ResolveWorksheetEntryName(archive, "BD GN")), "G12"));
        Assert.NotEmpty(worksheet.Descendants(SpreadsheetNamespace + "mergeCell"));
        Assert.NotEmpty(ReadXml(archive, "xl/workbook.xml").Descendants(SpreadsheetNamespace + "definedName"));
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("xl/media/fp-pro-item-", StringComparison.Ordinal));
        AssertImageAnchoredInsideFirstItemArea(archive, worksheet, request.Items[0].ImageBase64);
        AssertWorkbookForcesFullCalculation(archive);
    }

    [Fact]
    public async Task GenerateAsync_WithAlternativeGlobalPercentages_WritesExcelPercentageFractions()
    {
        var request = await CreateSg648Item01RequestAsync();
        request = request with
        {
            Report = new FpProQuotationReportInput(
                request.Report.Order,
                request.Report.Description,
                request.Report.Location,
                15m,
                74.5m,
                5m,
                request.Report.ProfileBarCount,
                request.Report.DoorCount)
        };
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var quotationSheetName = ReadSheetNames(archive)
            .Single(value => value.StartsWith("COTIZ", StringComparison.OrdinalIgnoreCase));
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, quotationSheetName));
        Assert.Equal("0.15", ReadCell(archive, worksheet, "T13"));
        Assert.Equal("0.745", ReadCell(archive, worksheet, "AG13"));
        Assert.Equal("0.05", ReadCell(archive, worksheet, "AI13"));
    }

    [Theory]
    [InlineData("Bracamonte LT5", "Bracamonte LT5.xlsx")]
    [InlineData("Bracamonte LT5.xlsx", "Bracamonte LT5.xlsx")]
    [InlineData("Casa/Prueba:*?\"<>|", "CasaPrueba.xlsx")]
    public async Task GenerateAsync_UsesSanitizedProposalNameAsWorkbookFileName(
        string proposalName,
        string expectedFileName)
    {
        var request = (await CreateSg648Item01RequestAsync()) with { ProposalName = proposalName };
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedFileName, workbook.FileName);
    }

    [Fact]
    public async Task GenerateAsync_WithThreeItems_WritesEachPreparedBlockInRequestOrder()
    {
        var request = await CreateFixtureRequestAsync("S&G648_t2.PDF", "S&G648", take: 3);
        request = request with
        {
            Items =
            [
                request.Items[0] with { Module = 3m },
                request.Items[1] with { Module = 5m },
                request.Items[2]
            ]
        };
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        AssertItemWritten(archive, worksheet, request.Items[0], 15);
        AssertItemWritten(archive, worksheet, request.Items[1], 22);
        AssertItemWritten(archive, worksheet, request.Items[2], 29);
        Assert.Equal("3", ReadCell(archive, worksheet, "BE15"));
        Assert.Equal("5", ReadCell(archive, worksheet, "BE22"));
        Assert.Equal("J22*K22*M22", ReadFormula(worksheet, "L22"));
        Assert.Equal("J29*K29*M29", ReadFormula(worksheet, "L29"));
        AssertFormulaHasCachedValue(worksheet, "S27");
        AssertFormulaHasCachedValue(worksheet, "BN22");
        using (var templateArchive = new ZipArchive(File.OpenRead(TemplatePath()), ZipArchiveMode.Read))
        {
            var templateWorksheet = ReadXml(templateArchive, ResolveWorksheetEntryName(templateArchive, QuotationSheetName(templateArchive)));
            AssertFormulaPreservedWithCachedValue(templateWorksheet, worksheet, "S27");
            AssertFormulaPreservedWithCachedValue(templateWorksheet, worksheet, "BN22");
            AssertBlockFormulaCellsReadyForRecalculation(templateWorksheet, worksheet, 22);
        }

        AssertFpProImages(archive, worksheet, request.Items);
    }

    [Fact]
    public async Task GenerateAsync_WithSg648Item01_WritesFormulaCachedValuesAfterRoundTrip()
    {
        var request = await CreateSg648Item01RequestAsync();
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        await using var output = new MemoryStream(workbook.Content);
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        Assert.Equal("J15*K15*2.5*BJ15", ReadFormula(worksheet, "BL15"));
        Assert.Equal("364", ReadCell(archive, worksheet, "BL15"));
        Assert.Equal("BK15+BL15", ReadFormula(worksheet, "BM15"));
        Assert.Equal("382.967", ReadCell(archive, worksheet, "BM15"));
        AssertFormulaHasCachedValue(worksheet, "S20");
        AssertFormulaHasCachedValue(worksheet, "BN15");
        Assert.NotEmpty(ReadCell(archive, worksheet, "S20"));
        Assert.NotEmpty(ReadCell(archive, worksheet, "BN15"));
        Assert.Null(ReadCellElement(worksheet, "BL15").Attribute("t"));
        Assert.Null(ReadCellElement(worksheet, "BM15").Attribute("t"));
    }

    [Fact]
    public async Task GenerateAsync_WithRealFpProFixtures_GeneratesFullPreparedWorkbooks()
    {
        await AssertFullFixtureGeneratesAsync("S&G648_t2.PDF", "S&G648", 28, 22m, "S&G648_Fase2C.xlsx");
        await AssertFullFixtureGeneratesAsync("S&G1043.PDF", "S&G1043", 13, 20m, "S&G1043_Fase2C.xlsx");
        await AssertFullFixtureGeneratesAsync("S&G1049.PDF", "S&G1049", 40, 15m, "S&G1049_Fase2C.xlsx");
    }

    [Fact]
    public async Task SetTransportGlobalCorrection_WhenBaseIsUnderMinimum_DistributesPositiveCorrection()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var transport = ReadTransportRowsFromTemplate(TemplatePath())
            .First(value => value.MinimumTransport > 0m && value.RatePerKg > 0m);
        var request = CreateRequestWithSyntheticItems(
            fixtureRequest,
            transport.Location,
            new[]
            {
                BuildSyntheticItem(fixtureRequest.Items[0], "01", widthM: 0m, heightM: 0m, quantity: 1, selectedThicknessMm: 1m, structureWeightKg: 0m)
            });

        var generator = new QuotationWorkbookGenerator();
        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var correctionExpected = transport.MinimumTransport / request.Items.Sum(item => item.Quantity);

        Assert.Equal("01", ReadCell(archive, worksheet, "A15"));
        var correction = decimal.Parse(ReadCell(archive, worksheet, "BR338"), NumberStyles.Number, CultureInfo.InvariantCulture);
        Assert.Equal(Math.Round(correctionExpected, 2), Math.Round(correction, 2));
        Assert.True(correctionExpected > 0m);
    }

    [Fact]
    public async Task SetTransportGlobalCorrection_WhenBaseIsOverMinimum_SetsZeroCorrection()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var transport = ReadTransportRowsFromTemplate(TemplatePath())
            .First(value => value.MinimumTransport > 0m && value.RatePerKg > 0m);
        var itemWeight = (transport.MinimumTransport / transport.RatePerKg) + 1_000m;
        var request = CreateRequestWithSyntheticItems(
            fixtureRequest,
            transport.Location,
            new[]
            {
                BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    "01",
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 1,
                    selectedThicknessMm: 1m,
                    structureWeightKg: itemWeight)
            });

        var generator = new QuotationWorkbookGenerator();
        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var cellValue = ReadCell(archive, worksheet, "BR338");
        var correction = decimal.Parse(cellValue, NumberStyles.Number, CultureInfo.InvariantCulture);

        Assert.Equal(0m, correction);
    }

    [Fact]
    public async Task SetTransportGlobalCorrection_WithQuantityGreaterThanOne_UsesTotalStructures()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var transport = ReadTransportRowsFromTemplate(TemplatePath())
            .First(value => value.MinimumTransport > 0m && value.RatePerKg > 0m);
        var request = CreateRequestWithSyntheticItems(
            fixtureRequest,
            transport.Location,
            new[]
            {
                BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    "01",
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 2,
                    selectedThicknessMm: 1m,
                    structureWeightKg: 0m),
                BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    "02",
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 3,
                    selectedThicknessMm: 1m,
                    structureWeightKg: 0m)
            });

        var generator = new QuotationWorkbookGenerator();
        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var br338 = decimal.Parse(ReadCell(archive, worksheet, "BR338"), NumberStyles.Number, CultureInfo.InvariantCulture);

        var totalStructures = request.Items.Sum(item => item.Quantity);
        var expected = (transport.MinimumTransport - 0m) / totalStructures;
        Assert.Equal(expected, br338);
    }

    [Fact]
    public async Task SetTransportGlobalCorrection_UsesExactFormulaForBr338()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var transport = ReadTransportRowsFromTemplate(TemplatePath())
            .First(value => value.MinimumTransport > 0m && value.RatePerKg > 0m);
        var request = CreateRequestWithSyntheticItems(
            fixtureRequest,
            transport.Location,
            new[]
            {
                BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    "01",
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 1,
                    selectedThicknessMm: 1m,
                    structureWeightKg: 1m),
                BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    "02",
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 1,
                    selectedThicknessMm: 1m,
                    structureWeightKg: 2m)
            });
        var baseTransportTotal = request.Items.Sum(
            item => transport.RatePerKg * (item.StructureWeightKg + (item.WidthM * item.HeightM * 2.5m * item.SelectedThicknessMm)) * item.Quantity);
        var expectedCorrection = baseTransportTotal >= transport.MinimumTransport
            ? 0m
            : (transport.MinimumTransport - baseTransportTotal) / request.Items.Sum(item => item.Quantity);
        var generator = new QuotationWorkbookGenerator();
        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var correction = decimal.Parse(ReadCell(archive, worksheet, "BR338"), NumberStyles.Number, CultureInfo.InvariantCulture);

        Assert.Equal(expectedCorrection, correction);
    }

    [Fact]
    public async Task SetTransportGlobalCorrection_ForSg1088LikeCase_ApproximatesExpectedValues()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var transportRows = ReadTransportRowsFromTemplate(TemplatePath());
        var targetRow = transportRows.FirstOrDefault(
            value => Math.Abs(value.MinimumTransport - 1_800_000m) <= 1m
                     && Math.Abs(value.RatePerKg - 428_571.428m) <= 0.01m);
        if (targetRow is null)
        {
            return;
        }

        var expectedBaseTransportTotal = 742_422.86m;
        var expectedItem16BaseTransport = 68_020.71m;
        var totalWeight = expectedBaseTransportTotal / targetRow.RatePerKg;
        var item16Weight = expectedItem16BaseTransport / targetRow.RatePerKg;
        var remainingWeight = totalWeight - item16Weight;
        var otherWeight = remainingWeight / 22m;

        var syntheticItems = new List<FpProQuotationItemInput>
        {
            BuildSyntheticItem(
                fixtureRequest.Items[0],
                "16",
                widthM: 0m,
                heightM: 0m,
                quantity: 1,
                selectedThicknessMm: 1m,
                structureWeightKg: item16Weight)
        };
        syntheticItems.AddRange(
            Enumerable.Range(1, 22)
                .Select(index => BuildSyntheticItem(
                    fixtureRequest.Items[0],
                    (index + 1).ToString("00"),
                    widthM: 0m,
                    heightM: 0m,
                    quantity: 1,
                    selectedThicknessMm: 1m,
                    structureWeightKg: otherWeight)));

        var request = CreateRequestWithSyntheticItems(
            fixtureRequest,
            targetRow.Location,
            syntheticItems.ToArray());

        var generator = new QuotationWorkbookGenerator();
        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));

        var br338 = decimal.Parse(ReadCell(archive, worksheet, "BR338"), NumberStyles.Number, CultureInfo.InvariantCulture);
        Assert.Equal(45_981.61m, Math.Round(br338, 2), precision: 2);

        var item16Bm = decimal.Parse(ReadCell(archive, worksheet, "BM15"), NumberStyles.Number, CultureInfo.InvariantCulture);
        var expectedBn16 = (targetRow.RatePerKg * item16Bm) + br338;
        var item16FinalTransport = decimal.Parse(ReadCell(archive, worksheet, "BN15"), NumberStyles.Number, CultureInfo.InvariantCulture);
        Assert.Equal(114_002.33m, Math.Round(item16FinalTransport, 2), precision: 2);
        Assert.Equal(Math.Round(expectedBn16, 2), Math.Round(item16FinalTransport, 2));
    }

    [Fact]
    public void TransportComputation_DoesNotHardcodeAntqLocation()
    {
        var source = File.ReadAllText(
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Infrastructure",
                "Proposals",
                "FpPro",
                "QuotationWorkbookGenerator.cs")));
        Assert.DoesNotContain("ANTQ", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_WithAntqLocation_SetsOnlyMatchingViaticsAndIntermunicipalRows()
    {
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var request = CreateRequestWithSyntheticItems(fixtureRequest, "ANTQ", fixtureRequest.Items);
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var templateArchive = new ZipArchive(File.OpenRead(TemplatePath()), ZipArchiveMode.Read);
        var templateWorksheet = ReadXml(templateArchive, ResolveWorksheetEntryName(templateArchive, QuotationSheetName(templateArchive)));
        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var projectDays = ReadCell(archive, worksheet, "BT14");

        Assert.Equal("ANTQ", ReadCell(archive, worksheet, "C12"));
        Assert.False(string.IsNullOrWhiteSpace(projectDays));
        AssertOnlySelectedLogisticsRowChanged(
            templateArchive,
            templateWorksheet,
            archive,
            worksheet,
            "VIATICOS",
            "ANTQ",
            ["N DIAS", "DIAS"],
            projectDays);
        AssertOnlySelectedLogisticsRowChanged(
            templateArchive,
            templateWorksheet,
            archive,
            worksheet,
            "TRANSPORTES INTERMUNICIPALES",
            "ANTQ",
            ["N RETORNOS", "RETORNOS", "N RETORNO"],
            "1");
    }

    [Fact]
    public async Task GenerateAsync_WithAlternativeLocation_SetsLogisticsRowsWithoutHardcodedAntq()
    {
        using var templateArchive = new ZipArchive(File.OpenRead(TemplatePath()), ZipArchiveMode.Read);
        var templateWorksheet = ReadXml(templateArchive, ResolveWorksheetEntryName(templateArchive, QuotationSheetName(templateArchive)));
        var transportLocations = ReadTransportRowsFromTemplate(TemplatePath())
            .Select(value => value.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var viaticsLocations = ReadLogisticsRows(
                templateArchive,
                templateWorksheet,
                "VIATICOS",
                ["N DIAS", "DIAS"])
            .Select(value => value.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intermunicipalLocations = ReadLogisticsRows(
                templateArchive,
                templateWorksheet,
                "TRANSPORTES INTERMUNICIPALES",
                ["N RETORNOS", "RETORNOS", "N RETORNO"])
            .Select(value => value.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alternativeLocation = transportLocations
            .Where(value => !string.Equals(value, "ANTQ", StringComparison.OrdinalIgnoreCase))
            .First(value => viaticsLocations.Contains(value) && intermunicipalLocations.Contains(value));
        var fixtureRequest = await CreateSg648Item01RequestAsync();
        var request = CreateRequestWithSyntheticItems(fixtureRequest, alternativeLocation, fixtureRequest.Items);
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(request, TestContext.Current.CancellationToken);

        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        var projectDays = ReadCell(archive, worksheet, "BT14");

        Assert.Equal(alternativeLocation, ReadCell(archive, worksheet, "C12"));
        Assert.False(string.IsNullOrWhiteSpace(projectDays));
        AssertOnlySelectedLogisticsRowChanged(
            templateArchive,
            templateWorksheet,
            archive,
            worksheet,
            "VIATICOS",
            alternativeLocation,
            ["N DIAS", "DIAS"],
            projectDays);
        AssertOnlySelectedLogisticsRowChanged(
            templateArchive,
            templateWorksheet,
            archive,
            worksheet,
            "TRANSPORTES INTERMUNICIPALES",
            alternativeLocation,
            ["N RETORNOS", "RETORNOS", "N RETORNO"],
            "1");
    }

    private static async Task<QuotationWorkbookRequest> CreateSg648Item01RequestAsync()
    {
        return await CreateFixtureRequestAsync("S&G648_t2.PDF", "S&G648", take: 1);
    }

    private sealed record TransportLocationRow(string Location, decimal MinimumTransport, decimal RatePerKg);

    private sealed record LogisticsRow(string Location, string TargetCell, string Value);

    private static QuotationWorkbookRequest CreateRequestWithSyntheticItems(
        QuotationWorkbookRequest sourceRequest,
        string location,
        IReadOnlyList<FpProQuotationItemInput> items)
    {
        return sourceRequest with
        {
            Report = sourceRequest.Report with
            {
                Location = location
            },
            Items = items.ToArray()
        };
    }

    private static FpProQuotationItemInput BuildSyntheticItem(
        FpProQuotationItemInput source,
        string itemNumber,
        decimal widthM,
        decimal heightM,
        int quantity,
        decimal selectedThicknessMm,
        decimal structureWeightKg) =>
        source
            with
            {
                ItemNumber = itemNumber,
                WidthM = widthM,
                HeightM = heightM,
                Quantity = quantity,
                SelectedThicknessMm = selectedThicknessMm,
                StructureWeightKg = structureWeightKg
            };

    private static IReadOnlyList<TransportLocationRow> ReadTransportRowsFromTemplate(string templatePath)
    {
        using var template = new ZipArchive(File.OpenRead(templatePath), ZipArchiveMode.Read);
        var worksheet = ReadXml(template, ResolveWorksheetEntryName(template, "BD GN"));
        var sharedStrings = ReadSharedStrings(template);
        var headerRow = worksheet.Descendants(SpreadsheetNamespace + "row")
            .Where(row => int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber) && rowNumber <= 20)
            .FirstOrDefault(row =>
                row.Elements(SpreadsheetNamespace + "c")
                    .Select(cell => NormalizeHeaderText(NormalizeCell(cell, sharedStrings)))
                    .Any(value => string.Equals(value, "CIUDAD", StringComparison.Ordinal))
                && row.Elements(SpreadsheetNamespace + "c")
                    .Select(cell => NormalizeHeaderText(NormalizeCell(cell, sharedStrings)))
                    .Any(value => value == "VR VIAJE" || value == "TOTAL MINIMO" || value == "MINIMO")
                && row.Elements(SpreadsheetNamespace + "c")
                    .Select(cell => NormalizeHeaderText(NormalizeCell(cell, sharedStrings)))
                    .Any(value => string.Equals(value, "VR KG", StringComparison.Ordinal)));

        var headers = headerRow!.Elements(SpreadsheetNamespace + "c")
            .Select(cell => new
            {
                Column = new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()),
                Header = NormalizeHeaderText(NormalizeCell(cell, sharedStrings))
            })
            .ToDictionary(value => value.Column, value => value.Header, StringComparer.Ordinal);
        var locationColumn = headers.First(value => value.Value == "CIUDAD").Key;
        var minimumColumn = headers.First(value => value.Value == "VR VIAJE" || value.Value == "TOTAL MINIMO" || value.Value == "MINIMO").Key;
        var rateColumn = headers.First(value => value.Value == "VR KG").Key;

        return worksheet
            .Descendants(SpreadsheetNamespace + "row")
            .Where(row => int.TryParse(row.Attribute("r")?.Value, out var rowNumber) && rowNumber > 1)
            .Select(row =>
            {
                var location = ReadCellValueFromRow(row, locationColumn, sharedStrings).Trim();
                if (string.IsNullOrWhiteSpace(location))
                {
                    return null;
                }

                if (!decimal.TryParse(ReadCellValueFromRow(row, minimumColumn, sharedStrings), NumberStyles.Number, CultureInfo.InvariantCulture, out var minimumTransport))
                {
                    return null;
                }

                if (!decimal.TryParse(ReadCellValueFromRow(row, rateColumn, sharedStrings), NumberStyles.Number, CultureInfo.InvariantCulture, out var ratePerKg))
                {
                    return null;
                }

                return new TransportLocationRow(location, minimumTransport, ratePerKg);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static void AssertOnlySelectedLogisticsRowChanged(
        ZipArchive templateArchive,
        XDocument templateWorksheet,
        ZipArchive generatedArchive,
        XDocument generatedWorksheet,
        string blockTitle,
        string location,
        IReadOnlyList<string> valueHeaderCandidates,
        string expectedValue)
    {
        var templateRows = ReadLogisticsRows(templateArchive, templateWorksheet, blockTitle, valueHeaderCandidates)
            .ToDictionary(value => value.Location, StringComparer.OrdinalIgnoreCase);
        var generatedRows = ReadLogisticsRows(generatedArchive, generatedWorksheet, blockTitle, valueHeaderCandidates)
            .ToDictionary(value => value.Location, StringComparer.OrdinalIgnoreCase);

        Assert.True(generatedRows.TryGetValue(location, out var selectedRow), $"No se encontro {location} en {blockTitle}.");
        AssertLogisticsValue(expectedValue, selectedRow.Value);

        foreach (var generatedRow in generatedRows.Values.Where(value => !string.Equals(value.Location, location, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.True(templateRows.TryGetValue(generatedRow.Location, out var templateRow), $"No se encontro {generatedRow.Location} en plantilla.");
            Assert.Equal(templateRow.Value, generatedRow.Value);
        }
    }

    private static void AssertLogisticsValue(string expected, string actual)
    {
        if (decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber)
            && decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber))
        {
            Assert.Equal(expectedNumber, actualNumber, precision: 8);
            return;
        }

        Assert.Equal(expected, actual);
    }

    private static IReadOnlyList<LogisticsRow> ReadLogisticsRows(
        ZipArchive archive,
        XDocument worksheet,
        string blockTitle,
        IReadOnlyList<string> valueHeaderCandidates)
    {
        var sharedStrings = ReadSharedStrings(archive);
        var rows = worksheet.Descendants(SpreadsheetNamespace + "row").ToArray();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var rowTexts = rows[rowIndex].Elements(SpreadsheetNamespace + "c")
                .Select(cell => NormalizeLogisticsText(NormalizeCell(cell, sharedStrings)))
                .ToArray();
            var normalizedBlockTitle = NormalizeLogisticsText(blockTitle);
            if (!rowTexts.Any(text => text.Contains(normalizedBlockTitle, StringComparison.Ordinal)))
            {
                continue;
            }

            var header = FindLogisticsHeader(rows, rowIndex, sharedStrings, valueHeaderCandidates);
            var result = new List<LogisticsRow>();
            foreach (var row in rows.Skip(header.RowIndex + 1).Take(140))
            {
                var rowNumber = int.Parse(row.Attribute("r")!.Value, CultureInfo.InvariantCulture);
                var location = ReadCellValueFromRow(row, header.LocationColumn, sharedStrings).Trim();
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var targetCell = $"{header.TargetColumn}{rowNumber}";
                result.Add(new LogisticsRow(location, targetCell, ReadCellIfExists(archive, worksheet, targetCell)));
            }

            return result
                .GroupBy(value => value.Location, StringComparer.OrdinalIgnoreCase)
                .Select(value => value.First())
                .ToArray();
        }

        throw new InvalidDataException($"No se encontro el bloque {blockTitle}.");
    }

    private static (int RowIndex, string LocationColumn, string TargetColumn) FindLogisticsHeader(
        IReadOnlyList<XElement> rows,
        int blockRowIndex,
        IReadOnlyList<string> sharedStrings,
        IReadOnlyList<string> valueHeaderCandidates)
    {
        var normalizedValueHeaders = valueHeaderCandidates
            .Select(NormalizeLogisticsText)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedLocationHeaders = new[] { "CIUDAD", "UBICACION", "ZONA", "MUNICIPIO", "CITY" }
            .Select(NormalizeLogisticsText)
            .ToHashSet(StringComparer.Ordinal);

        for (var offset = 1; offset <= 20 && blockRowIndex + offset < rows.Count; offset++)
        {
            var rowIndex = blockRowIndex + offset;
            var headers = rows[rowIndex].Elements(SpreadsheetNamespace + "c")
                .Select(cell => (
                    Column: new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()),
                    Header: NormalizeLogisticsText(NormalizeCell(cell, sharedStrings))))
                .ToArray();
            var locationColumn = headers
                .Where(value => normalizedLocationHeaders.Contains(value.Header))
                .Select(value => value.Column)
                .FirstOrDefault();
            var targetColumn = headers
                .Where(value => normalizedValueHeaders.Contains(value.Header))
                .Select(value => value.Column)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(locationColumn) && !string.IsNullOrEmpty(targetColumn))
            {
                return (rowIndex, locationColumn, targetColumn);
            }
        }

        throw new InvalidDataException("No se encontro la fila de encabezado de logistica.");
    }

    private static string NormalizeCell(XElement? cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (cell.Attribute("t")?.Value == "s")
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex)
                && sharedIndex >= 0
                && sharedIndex < sharedStrings.Count
                    ? sharedStrings[sharedIndex]
                    : string.Empty;
        }

        return value;
    }

    private static string NormalizeHeaderText(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormD)
            .ToUpperInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static string NormalizeLogisticsText(string value) =>
        NormalizeHeaderText(value)
            .Replace("°", string.Empty, StringComparison.Ordinal)
            .Replace("º", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("/", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    private static string ReadCellValueFromRow(XElement row, string columnReference, IReadOnlyList<string> sharedStrings) =>
        NormalizeCell(row.Elements(SpreadsheetNamespace + "c")
                .FirstOrDefault(cell => string.Equals(new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()), columnReference, StringComparison.Ordinal)),
            sharedStrings);

    private static async Task<QuotationWorkbookRequest> CreateFixtureRequestAsync(
        string fileName,
        string order,
        int? take = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "FpPro", fileName);
        await using var stream = File.OpenRead(path);
        var parser = new FpProReportParser();
        var preview = await parser.ParseAsync(
            new FpProReportFile(fileName, PdfContentType, stream.Length, stream),
            TestContext.Current.CancellationToken);
        var resolver = new FpProPreviewConfigurationResolver(new QuotationTemplateCatalogReader());
        preview = await resolver.ResolveAsync(preview, TestContext.Current.CancellationToken);
        var items = take is null
            ? preview.Items
            : preview.Items.Take(take.Value).ToArray();

        return new QuotationWorkbookRequest(
            new FpProQuotationReportInput(order, preview.Report.Description, "BGA", preview.Report.AluminumWastePercent!.Value, 60m, 0m, preview.Report.ProfileBarCount,preview.Report.DoorCount),
            order,
            "Cliente S&G",
            preview.Report.Description ?? order,
            "SG ESENCIAL",
            "LUISA HERNANDEZ",
            order,
            items.Select(item => new FpProQuotationItemInput(
                    item.ItemNumber,
                    Required(item.Typology, item.ItemNumber, "typology"),
                    Required(item.WidthM, item.ItemNumber, "widthM"),
                    Required(item.HeightM, item.ItemNumber, "heightM"),
                    Required(item.Quantity, item.ItemNumber, "quantity"),
                    item.System ?? FixtureSystem,
                    item.GlassDescription ?? FixtureGlass,
                    item.Finish ?? FixtureFinish,
                    100000m,
                    Required(item.AccessoriesBase, item.ItemNumber, "accessoriesBase"),
                    Required(item.AluminumBase, item.ItemNumber, "aluminumBase"),
                    Required(item.SelectedThicknessMm, item.ItemNumber, "selectedThicknessMm"),
                    Required(item.StructureWeightKg, item.ItemNumber, "structureWeightKg"),
                    3m,
                    item.Notes,
                    Required(item.Image, item.ItemNumber, "image").Base64))
                .ToArray());
    }

    private static T Required<T>(T? value, string itemNumber, string field)
        where T : struct =>
        value ?? throw new InvalidOperationException($"Item {itemNumber} missing {field}.");

    private static T Required<T>(T? value, string itemNumber, string field)
        where T : class =>
        value ?? throw new InvalidOperationException($"Item {itemNumber} missing {field}.");

    private static async Task AssertFullFixtureGeneratesAsync(
        string fixtureName,
        string order,
        int expectedItems,
        decimal expectedAluminumWastePercent,
        string outputFileName)
    {
        var request = await CreateFixtureRequestAsync(fixtureName, order);
        Assert.Equal(expectedItems, request.Items.Count);
        Assert.Equal(expectedAluminumWastePercent, request.Report.AluminumWastePercent);
        var templatePath = TemplatePath();
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(
            templatePath,
            TestContext.Current.CancellationToken));
        var generator = new QuotationWorkbookGenerator();

        var workbook = await generator.GenerateAsync(
            request,
            TestContext.Current.CancellationToken);

        var outputPath = Path.Combine(Path.GetTempPath(), outputFileName);
        await File.WriteAllBytesAsync(outputPath, workbook.Content, TestContext.Current.CancellationToken);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(
            templatePath,
            TestContext.Current.CancellationToken)));
        using var archive = new ZipArchive(new MemoryStream(workbook.Content), ZipArchiveMode.Read);
        var worksheet = ReadXml(archive, ResolveWorksheetEntryName(archive, QuotationSheetName(archive)));
        AssertItemWritten(archive, worksheet, request.Items[0], 15);
        AssertItemWritten(archive, worksheet, request.Items[request.Items.Count / 2], 15 + ((request.Items.Count / 2) * 7));
        AssertItemWritten(archive, worksheet, request.Items[^1], 15 + ((request.Items.Count - 1) * 7));
        AssertFpProImages(archive, worksheet, request.Items);
        AssertAllUsedItemBlockFormulasHaveCachedValues(worksheet, request.Items.Count);
        AssertWorkbookForcesFullCalculation(archive);
        Assert.Equal((expectedAluminumWastePercent / 100m).ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, "T13"));
        if (order == "S&G648")
        {
            Assert.Equal("238", ReadCell(archive, worksheet, "T369"));
            Assert.Equal("8", ReadCell(archive, worksheet, "V369"));
            Assert.Equal("15.613", ReadCell(archive, worksheet, "BK50"));
            Assert.Equal("16.605", ReadCell(archive, worksheet, "BK64"));
            Assert.Equal("15.495", ReadCell(archive, worksheet, "BK113"));
        }
        if (order == "S&G1043")
        {
            Assert.Equal("147", ReadCell(archive, worksheet, "T369"));
            Assert.Equal("3", ReadCell(archive, worksheet, "V369"));
        }

        if (order == "S&G1049")
        {
            Assert.Equal("242", ReadCell(archive, worksheet, "T369"));
            Assert.Equal("12", ReadCell(archive, worksheet, "V369"));
        }
    }

    private static void AssertItemWritten(
        ZipArchive archive,
        XDocument worksheet,
        FpProQuotationItemInput item,
        int row)
    {
        Assert.Equal(item.ItemNumber, ReadCell(archive, worksheet, $"A{row}"));
        Assert.Equal(item.Typology, ReadCell(archive, worksheet, $"B{row}"));
        Assert.Equal(item.System, ReadCell(archive, worksheet, $"D{row}"));
        Assert.Equal(item.GlassDescription, ReadCell(archive, worksheet, $"D{row + 1}"));
        Assert.Equal(item.Finish, ReadCell(archive, worksheet, $"D{row + 2}"));
        Assert.Equal(item.WidthM.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"J{row}"));
        Assert.Equal(item.HeightM.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"K{row}"));
        Assert.Equal(item.Quantity.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"M{row}"));
        Assert.Equal(item.AccessoriesBase.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"Q{row + 1}"));
        Assert.Equal(item.AluminumBase.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"R{row + 1}"));
        Assert.Equal(item.GlassPrice.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"S{row + 1}"));
        Assert.Equal(item.Module.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"BE{row}"));
        Assert.Equal(item.SelectedThicknessMm.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"BJ{row}"));
        Assert.Equal(item.StructureWeightKg.ToString(CultureInfo.InvariantCulture), ReadCell(archive, worksheet, $"BK{row}"));
        Assert.Equal($"J{row}*K{row}*M{row}", ReadFormula(worksheet, $"L{row}"));
        Assert.Equal($"J{row}*K{row}*2.5*BJ{row}", ReadFormula(worksheet, $"BL{row}"));
        Assert.Equal($"BK{row}+BL{row}", ReadFormula(worksheet, $"BM{row}"));
    }

    private static void AssertFpProImages(
        ZipArchive archive,
        XDocument worksheet,
        IReadOnlyList<FpProQuotationItemInput> items)
    {
        Assert.Equal(items.Count, archive.Entries.Count(entry =>
            entry.FullName.StartsWith("xl/media/fp-pro-item-", StringComparison.Ordinal)));
        var mediaHashes = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/media/fp-pro-item-", StringComparison.Ordinal))
            .Select(entry => Convert.ToHexString(SHA256.HashData(ReadEntryBytes(entry))))
            .ToArray();
        foreach (var item in items)
        {
            var originalHash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(item.ImageBase64)));
            Assert.Contains(originalHash, mediaHashes);
        }

        var drawing = ReadXml(archive, "xl/drawings/drawing1.xml");
        var anchors = drawing.Descendants(DrawingNamespace + "oneCellAnchor")
            .Where(anchor => anchor.Descendants(DrawingNamespace + "cNvPr")
                .Any(value => value.Attribute("name")?.Value.StartsWith("FP Pro Item ", StringComparison.Ordinal) == true))
            .ToArray();
        Assert.Equal(items.Count, anchors.Length);
        var drawingIds = anchors
            .Select(anchor => anchor.Descendants(DrawingNamespace + "cNvPr").Single().Attribute("id")!.Value)
            .ToArray();
        Assert.Equal(items.Count, drawingIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Enumerable.Range(0, items.Count), index =>
            Assert.Contains((1001 + index).ToString(CultureInfo.InvariantCulture), drawingIds));

        var relationshipIds = ReadDrawingRelationships(archive)
            .Root!.Elements(PackageRelationshipNamespace + "Relationship")
            .Where(value => value.Attribute("Target")?.Value.Contains("fp-pro-item-", StringComparison.Ordinal) == true)
            .Select(value => value.Attribute("Id")!.Value)
            .ToArray();
        Assert.Equal(relationshipIds.Length, relationshipIds.Distinct(StringComparer.Ordinal).Count());
        for (var index = 0; index < items.Count; index++)
        {
            AssertImageAnchorInsideItemArea(worksheet, anchors[index], items[index].ImageBase64, 15 + (index * 7));
        }
    }

    private static void AssertImageAnchoredInsideFirstItemArea(
        ZipArchive archive,
        XDocument worksheet,
        string imageBase64)
    {
        var drawing = ReadXml(archive, "xl/drawings/drawing1.xml");
        var anchor = drawing.Descendants(DrawingNamespace + "oneCellAnchor")
            .Last();
        AssertImageAnchorInsideItemArea(worksheet, anchor, imageBase64, 15);

        var relationships = ReadXml(archive, "xl/drawings/_rels/drawing1.xml.rels");
        Assert.Contains(relationships.Root!.Elements(PackageRelationshipNamespace + "Relationship"), value =>
            value.Attribute("Target")?.Value.Contains("fp-pro-item-", StringComparison.Ordinal) == true);
    }

    private static void AssertImageAnchorInsideItemArea(
        XDocument worksheet,
        XElement anchor,
        string imageBase64,
        int row)
    {
        var imageBytes = Convert.FromBase64String(imageBase64);
        var (imageWidth, imageHeight) = ReadPngDimensions(imageBytes);
        var availableWidthPixels = ColumnWidthToPixels(ReadColumnWidth(worksheet, 1))
            + ColumnWidthToPixels(ReadColumnWidth(worksheet, 2));
        var visualStartRow = row + 2;
        var availableHeightPixels = Enumerable.Range(visualStartRow, 4)
            .Select(value => RowHeightToPixels(ReadRowHeight(worksheet, value)))
            .Sum();
        var expectedScale = Math.Min(availableWidthPixels / imageWidth, availableHeightPixels / imageHeight);
        var expectedWidthEmu = PixelsToEmu(imageWidth * expectedScale);
        var expectedHeightEmu = PixelsToEmu(imageHeight * expectedScale);
        var expectedHorizontalOffsetEmu = PixelsToEmu((availableWidthPixels - (imageWidth * expectedScale)) / 2);
        var expectedVerticalOffsetEmu = PixelsToEmu((availableHeightPixels - (imageHeight * expectedScale)) / 2);

        var from = anchor.Element(DrawingNamespace + "from")!;
        var colOff = long.Parse(from.Element(DrawingNamespace + "colOff")!.Value, CultureInfo.InvariantCulture);
        var rowOff = long.Parse(from.Element(DrawingNamespace + "rowOff")!.Value, CultureInfo.InvariantCulture);
        var width = long.Parse(anchor.Element(DrawingNamespace + "ext")!.Attribute("cx")!.Value, CultureInfo.InvariantCulture);
        var height = long.Parse(anchor.Element(DrawingNamespace + "ext")!.Attribute("cy")!.Value, CultureInfo.InvariantCulture);

        Assert.Equal("0", anchor.Element(DrawingNamespace + "from")!.Element(DrawingNamespace + "col")!.Value);
        Assert.Equal((visualStartRow - 1).ToString(CultureInfo.InvariantCulture), anchor.Element(DrawingNamespace + "from")!.Element(DrawingNamespace + "row")!.Value);
        Assert.True(width > 0);
        Assert.True(height > 0);
        Assert.NotEqual(952500, width);
        Assert.NotEqual(952500, height);
        Assert.Equal(expectedWidthEmu, width);
        Assert.Equal(expectedHeightEmu, height);
        Assert.Equal(expectedHorizontalOffsetEmu, colOff);
        Assert.Equal(expectedVerticalOffsetEmu, rowOff);
        Assert.True(colOff >= 0);
        Assert.True(rowOff >= 0);
        Assert.True(colOff + width <= PixelsToEmu(availableWidthPixels));
        Assert.True(rowOff + height <= PixelsToEmu(availableHeightPixels));
        Assert.True(visualStartRow > row);
        Assert.True(visualStartRow + 4 <= row + 6);
        var xfrm = anchor.Descendants(MainDrawingNamespace + "xfrm").Single();
        Assert.NotEqual("10800000", xfrm.Attribute("rot")?.Value);
        Assert.Null(xfrm.Attribute("flipH"));
        Assert.Null(xfrm.Attribute("flipV"));
        Assert.Equal(
            imageWidth / (double)imageHeight,
            width / (double)height,
            precision: 2);
    }

    private static void AssertWorkbookForcesFullCalculation(ZipArchive archive)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var calcPr = workbook.Root!.Element(SpreadsheetNamespace + "calcPr");
        Assert.NotNull(calcPr);
        Assert.Equal("auto", calcPr!.Attribute("calcMode")?.Value);
        Assert.Equal("1", calcPr.Attribute("fullCalcOnLoad")?.Value);
        Assert.Equal("1", calcPr.Attribute("forceFullCalc")?.Value);
        Assert.Null(archive.GetEntry("xl/calcChain.xml"));
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        Assert.DoesNotContain(relationships.Root!.Elements(PackageRelationshipNamespace + "Relationship"), value =>
            value.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
    }

    private static void AssertFormulaPreservedWithCachedValue(
        XDocument templateWorksheet,
        XDocument generatedWorksheet,
        string reference)
    {
        var templateCell = ReadCellElement(templateWorksheet, reference);
        var generatedCell = ReadCellElement(generatedWorksheet, reference);
        Assert.Equal(ReadFormula(templateWorksheet, reference), ReadFormula(generatedWorksheet, reference));
        Assert.Equal(templateCell.Attribute("s")?.Value, generatedCell.Attribute("s")?.Value);
        Assert.Null(generatedCell.Element(SpreadsheetNamespace + "is"));
        Assert.NotNull(generatedCell.Element(SpreadsheetNamespace + "v"));
    }

    private static void AssertFormulaHasCachedValue(XDocument worksheet, string reference)
    {
        var cell = ReadCellElement(worksheet, reference);
        Assert.NotNull(cell.Element(SpreadsheetNamespace + "f"));
        Assert.NotNull(cell.Element(SpreadsheetNamespace + "v"));
        Assert.Null(cell.Element(SpreadsheetNamespace + "is"));
    }

    private static void AssertAllUsedItemBlockFormulasHaveCachedValues(XDocument worksheet, int itemCount)
    {
        for (var index = 0; index < itemCount; index++)
        {
            var baseRow = 15 + (index * 7);
            foreach (var formulaCell in worksheet.Descendants(SpreadsheetNamespace + "c")
                .Where(cell => cell.Element(SpreadsheetNamespace + "f") is not null)
                .Where(cell =>
                {
                    var reference = cell.Attribute("r")!.Value;
                    var rowNumber = int.Parse(new string(reference.Where(char.IsAsciiDigit).ToArray()), CultureInfo.InvariantCulture);
                    return rowNumber >= baseRow && rowNumber < baseRow + 7;
                }))
            {
                Assert.NotNull(formulaCell.Element(SpreadsheetNamespace + "v"));
                Assert.Null(formulaCell.Element(SpreadsheetNamespace + "is"));
            }
        }
    }

    private static void AssertBlockFormulaCellsReadyForRecalculation(
        XDocument templateWorksheet,
        XDocument generatedWorksheet,
        int row)
    {
        foreach (var templateCell in templateWorksheet.Descendants(SpreadsheetNamespace + "c")
            .Where(cell => cell.Element(SpreadsheetNamespace + "f") is not null)
            .Where(cell =>
            {
                var reference = cell.Attribute("r")!.Value;
                var rowNumber = int.Parse(new string(reference.Where(char.IsAsciiDigit).ToArray()), CultureInfo.InvariantCulture);
                return rowNumber >= row && rowNumber < row + 7;
            }))
        {
            var reference = templateCell.Attribute("r")!.Value;
            var generatedCell = ReadCellElement(generatedWorksheet, reference);
            Assert.Equal(templateCell.Element(SpreadsheetNamespace + "f")!.Value, generatedCell.Element(SpreadsheetNamespace + "f")!.Value);
            Assert.Equal(templateCell.Attribute("s")?.Value, generatedCell.Attribute("s")?.Value);
            Assert.Null(generatedCell.Element(SpreadsheetNamespace + "is"));
            Assert.NotNull(generatedCell.Element(SpreadsheetNamespace + "v"));
        }
    }

    private static string TemplatePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Infrastructure",
            "Resources",
            "Templates",
            "FormatoCotizacion.xlsx"));

    private static string[] ReadSheetNames(ZipArchive archive)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        return workbook.Descendants(SpreadsheetNamespace + "sheet")
            .Select(value => value.Attribute("name")!.Value)
            .ToArray();
    }

    private static string QuotationSheetName(ZipArchive archive) =>
        ReadSheetNames(archive).Single(value => value.StartsWith("COTIZ", StringComparison.OrdinalIgnoreCase));

    private static string ResolveWorksheetEntryName(ZipArchive archive, string sheetName)
    {
        var workbook = ReadXml(archive, "xl/workbook.xml");
        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheet = workbook.Descendants(SpreadsheetNamespace + "sheet")
            .First(value => value.Attribute("name")!.Value == sheetName);
        var id = sheet.Attribute(relNs + "id")!.Value;
        var target = relationships.Root!.Elements()
            .First(value => value.Attribute("Id")!.Value == id)
            .Attribute("Target")!.Value;

        return target.StartsWith("xl/", StringComparison.Ordinal)
            ? target
            : $"xl/{target.TrimStart('/')}";
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)!;
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string ReadFormula(XDocument worksheet, string reference) =>
        ReadCellElement(worksheet, reference).Element(SpreadsheetNamespace + "f")!.Value;

    private static string ReadCell(ZipArchive archive, XDocument worksheet, string reference)
    {
        var cell = ReadCellElement(worksheet, reference);
        return ReadCellValue(archive, cell);
    }

    private static string ReadCellIfExists(ZipArchive archive, XDocument worksheet, string reference)
    {
        var cell = worksheet.Descendants(SpreadsheetNamespace + "c")
            .FirstOrDefault(value => value.Attribute("r")?.Value == reference);
        return cell is null ? string.Empty : ReadCellValue(archive, cell);
    }

    private static string ReadCellValue(ZipArchive archive, XElement cell)
    {
        var inlineString = cell.Element(SpreadsheetNamespace + "is")
            ?.Element(SpreadsheetNamespace + "t")
            ?.Value;
        if (inlineString is not null)
        {
            return inlineString;
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
        if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var sharedIndex))
        {
            return ReadSharedStrings(archive)[sharedIndex];
        }

        return value;
    }

    private static XElement ReadCellElement(XDocument worksheet, string reference) =>
        worksheet.Descendants(SpreadsheetNamespace + "c")
            .First(value => value.Attribute("r")!.Value == reference);

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(SpreadsheetNamespace + "si")
            .Select(value => string.Concat(value.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static double ReadColumnWidth(XDocument worksheet, int columnIndex)
    {
        foreach (var column in worksheet.Descendants(SpreadsheetNamespace + "col"))
        {
            var min = int.Parse(column.Attribute("min")!.Value, CultureInfo.InvariantCulture);
            var max = int.Parse(column.Attribute("max")!.Value, CultureInfo.InvariantCulture);
            if (columnIndex >= min && columnIndex <= max)
            {
                return double.Parse(column.Attribute("width")!.Value, CultureInfo.InvariantCulture);
            }
        }

        return 8.43d;
    }

    private static double ReadRowHeight(XDocument worksheet, int rowIndex)
    {
        var row = worksheet.Descendants(SpreadsheetNamespace + "row")
            .FirstOrDefault(value => value.Attribute("r")?.Value == rowIndex.ToString(CultureInfo.InvariantCulture));
        return row?.Attribute("ht") is { } height
            ? double.Parse(height.Value, CultureInfo.InvariantCulture)
            : 15d;
    }

    private static double ColumnWidthToPixels(double width) =>
        Math.Truncate(((256d * width) + Math.Truncate(128d / 7d)) / 256d * 7d);

    private static double RowHeightToPixels(double heightPoints) =>
        heightPoints * 96d / 72d;

    private static long PixelsToEmu(double pixels) =>
        Convert.ToInt64(Math.Round(pixels * 9525, MidpointRounding.AwayFromZero));

    private static (int Width, int Height) ReadPngDimensions(byte[] bytes) =>
        (
            (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19],
            (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);

    private static XDocument ReadDrawingRelationships(ZipArchive archive) =>
        ReadXml(archive, "xl/drawings/_rels/drawing1.xml.rels");
}
