using Infrastructure.Proposals.FpPro;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Proposals;

public sealed class QuotationTemplateCatalogReaderTests
{
    [Fact]
    public async Task ReadAsync_LoadsBdGnSystemsGlassFinishAndLocks()
    {
        var reader = new QuotationTemplateCatalogReader();

        var catalog = await reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(catalog.Systems, value =>
            value.TechnicalName == "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5"
            && value.Label == "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO"
            && value.ExcelValue.Trim() == value.Label
            && value.Lock == "CIERRE EMBUTIDO DE IMPACTO AUTOMATICO");
        Assert.Contains(catalog.GlassDescriptions, value => value.Label == "COMPOSICION MONOLITICO TEMPLADO 5 MM INC");
        Assert.Contains(catalog.GlassDescriptions, value => value.Label == "COMPOSICION MONOLITICO TEMPLADO 6 MM INC");
        Assert.Contains(catalog.GlassDescriptions, value => value.Label == "COMPOSICION MONOLITICO TEMPLADO 8 MM INC");
        Assert.Contains(catalog.GlassDescriptions, value => value.Label == "COMPOSICION MONOLITICO TEMPLADO 10 MM INC");
        Assert.Contains(catalog.Finishes, value => value.Label == "ALUCOLOR POLIESTER NEGRO MATE PP13");
        Assert.Contains(catalog.Locations, value => value.Value == "BGA");
        Assert.Contains(catalog.Locations, value => value.Value == "BTA");
        Assert.Contains(catalog.Locations, value => value.Value == "ANTQ");
        Assert.Contains(catalog.Locations, value => value.Value == "VALLE");
        Assert.Contains(catalog.Locations, value => value.Value == "COSTA");
        Assert.DoesNotContain(catalog.Locations, value => value.Value == "622000");
        Assert.DoesNotContain(catalog.Locations, value => value.Value == "715000");
        Assert.DoesNotContain(catalog.Locations, value => value.Value == "872000");
        Assert.DoesNotContain(catalog.GlassDescriptions, value => string.Equals(value.Label, "CRISTALES", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog.Finishes, value => string.Equals(value.Label, "ACABADO ALUMINIO", StringComparison.OrdinalIgnoreCase));
        AssertNoEmptyOrDuplicateValues(catalog.Systems.Select(value => value.Label));
        AssertNoEmptyOrDuplicateValues(catalog.GlassDescriptions.Select(value => value.Label));
        AssertNoEmptyOrDuplicateValues(catalog.Finishes.Select(value => value.Label));
        AssertNoEmptyOrDuplicateValues(catalog.Locations.Select(value => value.Label));
    }

    [Fact]
    public async Task ReadAsync_PreservesExactExcelValueForMonzaSystem()
    {
        var reader = new QuotationTemplateCatalogReader();

        var catalog = await reader.ReadAsync(TestContext.Current.CancellationToken);

        var monza = Assert.Single(catalog.Systems, value =>
            value.Label == "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA");
        Assert.Equal("VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA", monza.Label);
        Assert.Equal(monza.Label, monza.ExcelValue.Trim());
    }

    private static void AssertNoEmptyOrDuplicateValues(IEnumerable<string> values)
    {
        var materialized = values.ToArray();
        Assert.DoesNotContain(materialized, string.IsNullOrWhiteSpace);
        Assert.Equal(materialized.Length, materialized.Distinct(StringComparer.Ordinal).Count());
    }
}
