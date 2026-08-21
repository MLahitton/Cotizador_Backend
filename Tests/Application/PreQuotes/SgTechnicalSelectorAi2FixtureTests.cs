using System.Text.Json;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class SgTechnicalSelectorAi2FixtureTests
{
    [Theory]
    [InlineData(
        "V-01",
        "S35",
        "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA",
        SgTechnicalSelectionRuleCodes.SystemProjectingSiena)]
    [InlineData(
        "PV-06",
        "K70",
        "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
        SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles)]
    [InlineData(
        "V-25",
        "K50",
        "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA",
        SgTechnicalSelectionRuleCodes.SystemSlidingWindowMonza)]
    public async Task RealAi2Fixture_ResolvesSystemFromTechnicalSignals(
        string reference,
        string expectedCode,
        string expectedDisplayName,
        string expectedRule)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile(
                "Tests/Fixtures/DocumentProcessing/ai2-real-ventaneria-puertas.json")));
        var element = document.RootElement.GetProperty("elements")
            .EnumerateArray()
            .Single(value => Text(value.GetProperty("reference")) == reference);

        var result = await Selector().SelectAsync(
            Input(element),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, result.SuggestedSystemCode);
        Assert.Equal(
            expectedDisplayName,
            Systems().Single(system => system.Code == expectedCode).Name);
        Assert.Equal(expectedRule, result.AppliedRuleCode);
        if (reference == "V-25")
        {
            Assert.True(result.RequiresReview);
            Assert.Contains(
                SgTechnicalSelectionReviewReasons.SpecialGeometryWithoutConstraints,
                result.ReviewReasons);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(50)]
    public async Task Selector_ProcessesAnyItemCountWithoutFixedLimit(int count)
    {
        var selector = Selector();
        var inputs = Enumerable.Range(0, count)
            .Select(_ => new SgTechnicalSelectionInput(
                "PROJECTING",
                "PROJECTING",
                600,
                1800,
                1.08m,
                2,
                1,
                1,
                null,
                null,
                ["LOWER_FIXED_PANEL", "ASSOCIATED_FIXED_PANEL"],
                "RECTANGULAR",
                null,
                "3831"))
            .ToArray();

        foreach (var input in inputs)
        {
            var result = await selector.SelectAsync(
                input,
                TestContext.Current.CancellationToken);
            Assert.Equal("S35", result.SuggestedSystemCode);
        }
    }

    private static SgTechnicalSelectionInput Input(JsonElement element)
    {
        var configuration = element.GetProperty("configuration");
        return new(
            Text(element.GetProperty("functional_type")),
            Text(configuration.GetProperty("operation")),
            Measurement(element, "width"),
            Measurement(element, "height"),
            DecimalMeasurement(element, "area"),
            IntValue(configuration.GetProperty("panel_count")),
            IntValue(configuration.GetProperty("movable_panel_count")),
            IntValue(configuration.GetProperty("fixed_panel_count")),
            TextOrNull(configuration, "modulation"),
            TextOrNull(configuration, "opening_direction"),
            configuration.GetProperty("special_features")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray(),
            TextOrNull(element.GetProperty("geometry"), "normalized_type"),
            null,
            RequestedSystemRaw(element),
            TextOrNull(configuration, "raw_description"));
    }

    private static string? RequestedSystemRaw(JsonElement element)
    {
        if (element.TryGetProperty("profiles", out var profiles)
            && profiles.ValueKind == JsonValueKind.Array)
        {
            foreach (var profile in profiles.EnumerateArray())
            {
                if (profile.TryGetProperty("code", out var code)
                    && Text(code) is { } value)
                {
                    return value;
                }
            }
        }

        return TextOrNull(element, "notes");
    }

    private static int? Measurement(JsonElement element, string type)
    {
        foreach (var measurement in element.GetProperty("measurements")
            .EnumerateArray())
        {
            if (TextOrNull(measurement, "type") == type
                && measurement.GetProperty("value").ValueKind == JsonValueKind.Number)
            {
                return measurement.GetProperty("value").GetInt32();
            }
        }

        return null;
    }

    private static decimal? DecimalMeasurement(JsonElement element, string type)
    {
        foreach (var measurement in element.GetProperty("measurements")
            .EnumerateArray())
        {
            if (TextOrNull(measurement, "type") == type
                && measurement.GetProperty("value").ValueKind == JsonValueKind.Number)
            {
                return measurement.GetProperty("value").GetDecimal();
            }
        }

        return null;
    }

    private static int? IntValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("value", out var nested)
            && nested.ValueKind == JsonValueKind.Number
            ? nested.GetInt32()
            : null;
    }

    private static string? TextOrNull(JsonElement value, string property) =>
        value.TryGetProperty(property, out var propertyValue)
            && propertyValue.ValueKind != JsonValueKind.Null
            ? Text(propertyValue)
            : null;

    private static string? Text(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in new[] { "normalized", "value", "raw" })
        {
            if (value.TryGetProperty(property, out var propertyValue)
                && propertyValue.ValueKind == JsonValueKind.String)
            {
                return propertyValue.GetString();
            }
        }

        return null;
    }

    private static DeterministicSgTechnicalSelector Selector() =>
        new(new Catalog(Systems()));

    private static IReadOnlyList<ProductSystemCatalogReadModel> Systems() =>
    [
        System("S35", "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA",
            "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4",
            "PROJECTING", "PRIMAVERA SIENA", "CLASSIC", "STANDARD"),
        System("K70", "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70",
            "SLIDING_DOOR", "VENECIA NAPOLES", "PREMIUM", "STANDARD"),
        System("SG_VEN70_POCKET_DOOR",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70 TIPO POKET",
            "SLIDING_DOOR", "VENECIA NAPOLES", "PREMIUM", "POCKET"),
        System("K50", "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA",
            "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 50",
            "SLIDING_WINDOW", "VENECIA MONZA", "PREMIUM", "STANDARD"),
        System("S50", "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO",
            "VENTANA CORREDIZA LINEA CLASSIC  SISTEMA PRIMAVERA SERIE SG 5",
            "SLIDING_WINDOW", "PRIMAVERA LAGO", "CLASSIC", "STANDARD"),
        System("K40", "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 40",
            "FIXED", "VENECIA FERMO", "PREMIUM", "STANDARD")
    ];

    private static ProductSystemCatalogReadModel System(
        string code,
        string displayName,
        string technicalName,
        string functionalType,
        string family,
        string commercialLine,
        string variant) =>
        new(
            Guid.NewGuid(),
            code,
            displayName,
            technicalName,
            family,
            functionalType,
            family,
            null,
            commercialLine,
            variant,
            true,
            true,
            true,
            true,
            false,
            true);

    private sealed class Catalog(
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
        : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveSelectableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(systems.SingleOrDefault(system =>
                system.Code == code));
    }

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
