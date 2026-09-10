using System.Globalization;
using System.Text;
using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro.Experimental;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Infrastructure.Proposals.FpPro;
using Xunit;

namespace Tests.Application.Proposals;

public sealed class FpProModuleInferenceServiceTests
{
    private const string PdfContentType = "application/pdf";

    [Fact]
    public void Infer_WithSingleFrameBelowSixMeters_AppliesMinimumThreeModules()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4.5m,
            heightMeters: 2.5m,
            glassQuantity: 1,
            profiles: [Frame("MARCO0058", 14m)]);

        var result = service.Infer(input);

        Assert.Equal(1, result.GlassModules);
        Assert.Equal(1, result.FrameModules);
        Assert.Equal(2, result.RawModules);
        Assert.Equal(3, result.FinalModules);
        Assert.Equal(FpProModuleInferenceConfidence.High, result.Confidence);
    }

    [Fact]
    public void Infer_WithSingleFrameWiderThanSixMeters_SplitsFrameBySixMeterBars()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 7.85m,
            heightMeters: 2m,
            glassQuantity: 4,
            profiles: [Frame("MARCO0047", 19.7m)]);

        var result = service.Infer(input);

        Assert.Equal(4, result.GlassModules);
        Assert.Equal(2, result.FrameModules);
        Assert.Equal(6, result.FinalModules);
    }

    [Fact]
    public void Infer_WithSingleFrameWiderThanTwelveMeters_UsesThreeFrameModules()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 12.5m,
            heightMeters: 2m,
            glassQuantity: 4,
            profiles: [Frame("MARCO1925", 29m)]);

        var result = service.Infer(input);

        Assert.Equal(4, result.GlassModules);
        Assert.Equal(3, result.FrameModules);
        Assert.Equal(7, result.FinalModules);
    }

    [Fact]
    public void Infer_WithSeveralIndependentFramesInSameProfile_EstimatesFrameCountFromLength()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4m,
            heightMeters: 2m,
            glassQuantity: 2,
            profiles: [Frame("MARCO2133", 16m)]);

        var result = service.Infer(input);

        Assert.Equal(2, result.GlassModules);
        Assert.Equal(2, result.FrameGroups.Single().EstimatedIndependentFrameCount);
        Assert.Equal(2, result.FrameModules);
        Assert.Equal(4, result.FinalModules);
    }

    [Fact]
    public void Infer_WithQuantityGreaterThanOne_NormalizesGlassAndProfileLengthPerUnit()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4.5m,
            heightMeters: 2.5m,
            quantity: 2,
            glassQuantity: 6,
            profiles: [Frame("MARCO0058", 28m)]);

        var result = service.Infer(input);

        Assert.Equal(3, result.GlassModules);
        Assert.Equal(1, result.FrameModules);
        Assert.Equal(4, result.FinalModules);
        Assert.Equal(14m, result.FrameGroups.Single().LengthMetersPerUnit);
    }

    [Fact]
    public void Infer_WithDividerProfiles_DoesNotCountThemAsFrameModules()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4.5m,
            heightMeters: 2.5m,
            glassQuantity: 1,
            profiles: [Profile("MULLION DIVISOR", "MULLION DIVISOR T103", 14m)]);

        var result = service.Infer(input);

        Assert.Equal(1, result.GlassModules);
        Assert.Equal(0, result.FrameModules);
        Assert.Equal(3, result.FinalModules);
        Assert.Contains("FRAME_PROFILES_NOT_DETECTED", result.Warnings);
    }

    [Fact]
    public void Infer_WithMixedGlassAndFrameGroups_SumsGlassAndFrameModules()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4.5m,
            heightMeters: 2.5m,
            glassQuantity: 3,
            profiles:
            [
                Frame("MARCO0058", 14m),
                Frame("MARCO0047", 14m),
                Profile("ALFAJIA", "ALFAJIA", 3m)
            ]);

        var result = service.Infer(input);

        Assert.Equal(3, result.GlassModules);
        Assert.Equal(2, result.FrameModules);
        Assert.Equal(5, result.FinalModules);
    }

    [Fact]
    public void Infer_WithAmbiguousFrameLength_RequiresManualModule()
    {
        var service = new FpProModuleInferenceService();
        var input = CreateInput(
            widthMeters: 4.5m,
            heightMeters: 2.5m,
            glassQuantity: 2,
            profiles: [Frame("MARCO0058", 9.1m)]);

        var result = service.Infer(input);

        Assert.Equal(FpProModuleInferenceConfidence.Ambiguous, result.Confidence);
        Assert.True(result.RequiresManualModule);
        Assert.Contains(result.FrameGroups.Single().Warnings, value => value == "FRAME_LENGTH_AMBIGUOUS");
    }

    [Fact]
    public async Task Infer_FromFpProFixtures_ValidatesAgainstHistoricalWorkbookModules()
    {
        var service = new FpProModuleInferenceService();
        var knownCases = new Dictionary<(string Order, string Item), int>
        {
            [("S&G1049", "03")] = 5,
            [("S&G1043", "04")] = 4,
            [("S&G1043", "05")] = 4,
            [("S&G1043", "06")] = 6,
            [("S&G648", "06")] = 5,
            [("S&G648", "18")] = 6
        };
        var pairs = DetectFixturePairs();
        Assert.Equal(3, pairs.Count);

        var rows = new List<string>
        {
            "Order,PDF,HistoricalXlsx,Item,ExpectedModules,InferredModules,Match,Confidence,RequiresManualModule,GlassModules,FrameModules,WidthMeters,HeightMeters,Quantity,RelevantProfiles,ProfileLengths,Warnings,FrameGroups"
        };
        var validations = new List<RealValidationRow>();

        foreach (var pair in pairs)
        {
            var preview = await ParseFixtureAsync(pair.Pdf);
            Assert.NotEmpty(preview.Items);
            Assert.Contains(preview.Items, item => item.TechnicalProfiles.Count > 0);

            var expectedModules = ReadExpectedModules(pair.Xlsx);

            foreach (var item in preview.Items)
            {
                var result = service.Infer(item);
                Assert.True(result.FinalModules >= 3);
                var expected = expectedModules.GetValueOrDefault(item.ItemNumber);
                var matches = expected.HasValue && expected.Value == result.FinalModules;
                validations.Add(new RealValidationRow(pair.Order, item.ItemNumber, expected, result));

                rows.Add(string.Join(
                    ',',
                    Csv(pair.Order),
                    Csv(pair.Pdf),
                    Csv(pair.Xlsx),
                    Csv(item.ItemNumber),
                    Csv(expected?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    result.FinalModules.ToString(CultureInfo.InvariantCulture),
                    matches.ToString(CultureInfo.InvariantCulture),
                    result.Confidence.ToString(),
                    result.RequiresManualModule.ToString(CultureInfo.InvariantCulture),
                    result.GlassModules.ToString(CultureInfo.InvariantCulture),
                    result.FrameModules.ToString(CultureInfo.InvariantCulture),
                    Csv(item.WidthM?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(item.HeightM?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(item.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(string.Join('|', item.TechnicalProfiles.Select(value => $"{value.Code}:{value.Description}"))),
                    Csv(string.Join('|', item.TechnicalProfiles.Select(value => $"{value.Code}:{value.TotalLengthMeters?.ToString(CultureInfo.InvariantCulture)}:{value.UnitLengthMeters?.ToString(CultureInfo.InvariantCulture)}:{value.Quantity?.ToString(CultureInfo.InvariantCulture)}"))),
                    Csv(string.Join('|', result.Warnings)),
                    Csv(string.Join('|', result.FrameGroups.Select(value => $"{value.ProfileKey}:{value.ModuleCount}:{value.EstimatedIndependentFrameCount}")))));
            }
        }

        var csvPath = Path.Combine(Path.GetTempPath(), "FpProModuleInferenceRealValidation.csv");
        await File.WriteAllLinesAsync(csvPath, rows, Encoding.UTF8, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(csvPath));

        foreach (var known in knownCases)
        {
            var actual = validations.SingleOrDefault(value => value.Order == known.Key.Order && value.Item == known.Key.Item);
            Assert.NotNull(actual);
            Assert.Equal(known.Value, actual.ExpectedModules);
            Assert.True(
                known.Value == actual.Result.FinalModules,
                $"Known case {known.Key.Order} item {known.Key.Item}: expected/inferred {known.Value}/{actual.Result.FinalModules}. See {csvPath}.");
            Assert.False(actual.Result.RequiresManualModule);
        }

        var evaluated = validations.Where(value => value.ExpectedModules.HasValue).ToArray();
        Assert.NotEmpty(evaluated);
        Assert.All(evaluated, value => Assert.True(value.Result.FinalModules >= 3));

        var highConfidence = evaluated
            .Where(value => value.Result.Confidence == FpProModuleInferenceConfidence.High)
            .ToArray();
        if (highConfidence.Length > 0)
        {
            Assert.True(highConfidence.Count(value => value.ExpectedModules == value.Result.FinalModules) <= highConfidence.Length);
        }

    }

    [Theory]
    [InlineData("MARCO0058", FpProProfileCategory.Frame)]
    [InlineData("11113036 MARCO CUERPO FIJO", FpProProfileCategory.Frame)]
    [InlineData("NAVE2295", FpProProfileCategory.LeafFrame)]
    [InlineData("MULLION DIVISOR", FpProProfileCategory.Divider)]
    [InlineData("PISAVIDRIO", FpProProfileCategory.GlazingBead)]
    [InlineData("ADAPTADOR", FpProProfileCategory.Adapter)]
    [InlineData("ENGANCHE", FpProProfileCategory.Adapter)]
    [InlineData("ALFAJIA", FpProProfileCategory.Divider)]
    public void ClassifyProfile_UsesDeterministicCategories(string description, FpProProfileCategory expected)
    {
        Assert.Equal(expected, FpProModuleInferenceService.ClassifyProfile(description, description));
    }

    private static FpProModuleInferenceInput CreateInput(
        decimal widthMeters,
        decimal heightMeters,
        int glassQuantity,
        IReadOnlyList<FpProModuleProfileUsage> profiles,
        int quantity = 1)
    {
        return new FpProModuleInferenceInput(
            "01",
            widthMeters,
            heightMeters,
            quantity,
            [new FpProModuleGlassPaneInput(glassQuantity)],
            profiles);
    }

    private static FpProModuleProfileUsage Frame(string profileKey, decimal lengthMeters)
    {
        return Profile(profileKey, profileKey, lengthMeters);
    }

    private static FpProModuleProfileUsage Profile(string profileKey, string description, decimal? lengthMeters)
    {
        return new FpProModuleProfileUsage(
            profileKey,
            description,
            lengthMeters,
            FpProProfileCategory.Other);
    }

    private static async Task<FpProReportPreviewData> ParseFixtureAsync(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "FpPro",
            fileName));

        await using var stream = File.OpenRead(path);
        var parser = new FpProReportParser();
        return await parser.ParseAsync(
            new FpProReportFile(fileName, PdfContentType, stream.Length, stream),
            TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<FpProFixturePair> DetectFixturePairs()
    {
        var fixtureRoot = FixtureRoot();
        var pdfs = Directory.GetFiles(fixtureRoot, "*.PDF").Select(Path.GetFileName).OfType<string>().ToArray();
        var workbooks = Directory.GetFiles(fixtureRoot, "*.xlsx").Select(Path.GetFileName).OfType<string>().ToArray();

        return
        [
            Pair("S&G648", "648"),
            Pair("S&G1043", "1043"),
            Pair("S&G1049", "1049")
        ];

        FpProFixturePair Pair(string order, string token)
        {
            var pdf = pdfs.Single(value => value.Contains(token, StringComparison.OrdinalIgnoreCase));
            var xlsx = workbooks.Single(value => value.Contains(token, StringComparison.OrdinalIgnoreCase));
            return new FpProFixturePair(order, pdf, xlsx);
        }
    }

    private static Dictionary<string, int?> ReadExpectedModules(string workbookName)
    {
        using var document = SpreadsheetDocument.Open(Path.Combine(FixtureRoot(), workbookName), false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook part missing.");
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().First(value =>
            value.Name?.Value?.StartsWith("COTIZ", StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidDataException("COTIZACION sheet missing.");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var expected = new Dictionary<string, int?>(StringComparer.Ordinal);

        for (var index = 0; index < 200; index++)
        {
            var row = 15 + (index * 7);
            var itemValue = ReadCellText(worksheetPart, sharedStrings, row, "A");
            var module = ReadNullableInt(worksheetPart, sharedStrings, row, "BE");
            if (string.IsNullOrWhiteSpace(itemValue) && module is null)
            {
                if (index > 60)
                {
                    break;
                }

                continue;
            }

            var itemNumber = NormalizeItemNumber(itemValue, index + 1);
            expected[itemNumber] = module;
        }

        return expected;
    }

    private static string ReadCellText(
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        int rowNumber,
        string columnName)
    {
        return ReadCellValue(worksheetPart, sharedStrings, rowNumber, columnName)?.Trim() ?? string.Empty;
    }

    private static int? ReadNullableInt(
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        int rowNumber,
        string columnName)
    {
        var text = ReadCellValue(worksheetPart, sharedStrings, rowNumber, columnName);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            return (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static string? ReadCellValue(
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        int rowNumber,
        string columnName)
    {
        var reference = string.Concat(columnName, rowNumber.ToString(CultureInfo.InvariantCulture));
        var row = worksheetPart.Worksheet.Descendants<Row>()
            .FirstOrDefault(value => value.RowIndex?.Value == rowNumber);
        var cell = row?.Elements<Cell>().FirstOrDefault(value =>
            string.Equals(value.CellReference?.Value, reference, StringComparison.OrdinalIgnoreCase));
        var rawValue = cell?.CellValue?.Text;
        if (rawValue is null)
        {
            return null;
        }

        if (cell?.DataType?.Value == CellValues.SharedString
            && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex))
        {
            return sharedStrings?.Elements<SharedStringItem>().ElementAtOrDefault(sharedStringIndex)?.InnerText;
        }

        return rawValue;
    }

    private static string NormalizeItemNumber(string? value, int fallback)
    {
        if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString("00", CultureInfo.InvariantCulture);
        }

        return fallback.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string FixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "FpPro"));
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record FpProFixturePair(string Order, string Pdf, string Xlsx);

    private sealed record RealValidationRow(
        string Order,
        string Item,
        int? ExpectedModules,
        FpProModuleInferenceResult Result);
}
