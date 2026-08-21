using System.Text.Json;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class HistoricalBdGnGlassCatalogInventoryTests
{
    private const string FixturePath =
        "Tests/Fixtures/HistoricalGlassCatalog/bd-gn-cristales-inventory.json";

    [Fact]
    public void Inventory_ContainsHistoricalBdGnCristalesScan()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile(FixturePath)));
        var root = document.RootElement;
        var summary = root.GetProperty("summary");
        var groups = root.GetProperty("groups").EnumerateArray().ToArray();

        Assert.Equal(
            @"C:\Users\mlahi\Desktop\Cotizaciones",
            root.GetProperty("corpus_path").GetString());
        Assert.Equal(66, summary.GetProperty("workbooks_found").GetInt32());
        Assert.Equal(64, summary.GetProperty("workbooks_ooxml_read").GetInt32());
        Assert.Equal(2, summary.GetProperty("workbooks_ole_skipped").GetInt32());
        Assert.Equal(1288, summary.GetProperty("rows_read").GetInt32());
        Assert.Equal(29, summary.GetProperty("distinct_raw_values").GetInt32());
        Assert.Equal(27, summary.GetProperty("distinct_normalized_values").GetInt32());

        Assert.Contains(groups, value => Normalized(value) ==
            "COMPOSICION MONOLITICO TEMPLADO 4 MM INC");
        Assert.Contains(groups, value => Normalized(value) ==
            "COMPOSICION MONOLITICO CRUDO 4 MM MINI BOREAL");
        Assert.Contains(groups, value => Normalized(value) ==
            "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL167");
        Assert.Contains(groups, value => Normalized(value) ==
            "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM GREEN");
        Assert.Contains(groups, value => Normalized(value) == "N.A.");

        var chamber = groups.Single(value => Normalized(value) ==
            "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM + TEMPLADO 6 MM INC");
        var rawValues = chamber.GetProperty("raw_values")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Contains(
            "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 + TEMPLADO 6 MM INC ",
            rawValues);
        Assert.Contains(
            "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM  + TEMPLADO 6 MM INC ",
            rawValues);
    }

    private static string Normalized(JsonElement group) =>
        group.GetProperty("normalized_value").GetString()!;

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

        throw new DirectoryNotFoundException(
            $"No se encontro el fixture del repositorio: {relativePath}");
    }
}
