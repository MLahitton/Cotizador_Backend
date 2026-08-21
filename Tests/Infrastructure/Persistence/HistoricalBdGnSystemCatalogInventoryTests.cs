using System.Text.Json;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class HistoricalBdGnSystemCatalogInventoryTests
{
    private const string FixturePath =
        "Tests/Fixtures/HistoricalSystemCatalog/bd-gn-systems-inventory.json";

    [Fact]
    public void Inventory_ContainsHistoricalBdGnSystemScan()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile(FixturePath)));
        var root = document.RootElement;
        var summary = root.GetProperty("summary");
        var headers = root.GetProperty("headers").EnumerateArray().ToArray();
        var groups = root.GetProperty("groups").EnumerateArray().ToArray();

        Assert.Equal(
            @"C:\Users\mlahi\Desktop\Cotizaciones",
            root.GetProperty("corpus_path").GetString());
        Assert.Equal(66, summary.GetProperty("workbooks_found").GetInt32());
        Assert.Equal(64, summary.GetProperty("workbooks_ooxml_read").GetInt32());
        Assert.Equal(2, summary.GetProperty("workbooks_ole_skipped").GetInt32());
        Assert.Equal(4368, summary.GetProperty("rows_read").GetInt32());
        Assert.Equal(81, summary.GetProperty("distinct_raw_values").GetInt32());
        Assert.Equal(77, summary.GetProperty("distinct_normalized_values").GetInt32());

        var header = Assert.Single(headers);
        Assert.Equal("SISTEMAS", header.GetProperty("normalized_header").GetString());
        Assert.Contains(
            "SISTEMAS",
            header.GetProperty("raw_headers").EnumerateArray()
                .Select(value => value.GetString()?.Trim()));
        Assert.Contains("B1", header.GetProperty("cells").EnumerateArray()
            .Select(value => value.GetString()));

        Assert.Contains(groups, value => Normalized(value) ==
            "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4");
        Assert.Contains(groups, value => Normalized(value) ==
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70");
        Assert.Contains(groups, value => Normalized(value) ==
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA REJILLA");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA PERGOLA SG");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA SG CLARABOYA");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA DESLIZANTE TWIN DN");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA PLEGABLE TAURO");
        Assert.Contains(groups, value => Normalized(value) == "SISTEMA APILABLE SIGMA");
        Assert.Contains(groups, value => Normalized(value) ==
            "DIVISIONES DE BANO CON ACCESORIOS EN ACERO INOXIDABLE");
        Assert.Contains(groups, value => Normalized(value) == "N.A");
    }

    [Fact]
    public void Inventory_CapturesTechnicalAndCommercialNamesFromSameBdGnRows()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile(FixturePath)));
        var samples = document.RootElement
            .GetProperty("target_row_samples")
            .EnumerateArray()
            .ToArray();

        AssertCommercialName(
            samples,
            "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4",
            "B",
            "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4",
            "C",
            "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA");
        AssertCommercialName(
            samples,
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70",
            "B",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70",
            "C",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES");
        AssertCommercialName(
            samples,
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
            "B",
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
            "C",
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO");
        AssertCommercialName(
            samples,
            "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50",
            "B",
            "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50",
            "C",
            "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA");
    }

    private static string Normalized(JsonElement group) =>
        group.GetProperty("normalized_value").GetString()!;

    private static void AssertCommercialName(
        IReadOnlyCollection<JsonElement> samples,
        string systemNormalizedValue,
        string technicalColumn,
        string technicalValue,
        string commercialColumn,
        string commercialValue)
    {
        var sample = samples.First(value =>
            value.GetProperty("system_normalized_value").GetString()
                == Normalize(systemNormalizedValue));
        var rowValues = sample.GetProperty("row_values").EnumerateArray().ToArray();

        Assert.Equal(
            technicalValue,
            ValueAt(rowValues, technicalColumn)?.Trim());
        Assert.Equal(
            commercialValue,
            ValueAt(rowValues, commercialColumn)?.Trim());
    }

    private static string? ValueAt(
        IReadOnlyCollection<JsonElement> rowValues,
        string column) => rowValues.SingleOrDefault(value =>
            value.GetProperty("column").GetString() == column)
            .GetProperty("value")
            .GetString();

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Trim().ToUpperInvariant().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries));

    private static string RepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(relativePath);
    }
}
