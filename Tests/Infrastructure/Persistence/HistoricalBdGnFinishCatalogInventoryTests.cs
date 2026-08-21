using System.Text.Json;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class HistoricalBdGnFinishCatalogInventoryTests
{
    private const string FixturePath =
        "Tests/Fixtures/HistoricalFinishCatalog/bd-gn-finishes-inventory.json";

    [Fact]
    public void Inventory_ContainsHistoricalBdGnFinishScan()
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
        Assert.Equal(386, summary.GetProperty("rows_read").GetInt32());
        Assert.Equal(8, summary.GetProperty("distinct_raw_values").GetInt32());
        Assert.Equal(7, summary.GetProperty("distinct_normalized_values").GetInt32());

        var header = Assert.Single(headers);
        Assert.Equal(
            "ACABADO ALUMINIO",
            header.GetProperty("normalized_header").GetString());
        Assert.Contains(
            "ACABADO ALUMINIO",
            header.GetProperty("raw_headers").EnumerateArray()
                .Select(value => value.GetString()?.Trim()));

        Assert.Contains(groups, value => Normalized(value) ==
            "ALUCOLOR POLIESTER NEGRO MATE PP13");
        Assert.Contains(groups, value => Normalized(value) ==
            "ALUCOLOR POLIESTER BLANCO PP003");
        Assert.Contains(groups, value => Normalized(value) ==
            "ALUCOLOR POLIESTER PINTURA GRIS");
        Assert.Contains(groups, value => Normalized(value) ==
            "ALUCOLOR POLIESTER PINTURA CHAMPANA");
        Assert.Contains(groups, value => Normalized(value) ==
            "ANODIZADO BLANCO MATE AN001");
        Assert.Contains(groups, value => Normalized(value) == "INOX");
        Assert.Contains(groups, value => Normalized(value) == "N.A");
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

        throw new DirectoryNotFoundException(relativePath);
    }
}
