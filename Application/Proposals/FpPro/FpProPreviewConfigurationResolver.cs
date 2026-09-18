using System.Globalization;
using System.Text;
using Application.Common.Abstractions.Proposals;

namespace Application.Proposals.FpPro;

public sealed class FpProPreviewConfigurationResolver(
    IQuotationTemplateCatalogReader catalogReader) : IFpProPreviewConfigurationResolver
{
    private const string GlassPriceField = "glassPrice";
    private const string SystemField = "system";
    private const string GlassDescriptionField = "glassDescription";
    private const string FinishField = "finish";
    private const string LockField = "lock";
    private const string ModuleField = "module";
    private const string DefaultFinish = "ALUCOLOR POLIESTER NEGRO MATE PP13";

    public async Task<FpProReportPreviewData> ResolveAsync(
        FpProReportPreviewData preview,
        CancellationToken cancellationToken)
    {
        var catalog = await catalogReader.ReadAsync(cancellationToken);
        var items = preview.Items.Select(item => ResolveItem(item, catalog)).ToArray();

        return preview with
        {
            Items = items,
            PendingFields = items.SelectMany(item => item.PendingFields)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static FpProPreviewItemData ResolveItem(
        FpProPreviewItemData item,
        QuotationTemplateCatalog catalog)
    {
        var system = ResolveSystem(item, catalog);
        var glassDescription = ResolveGlassDescription(item.SelectedThicknessMm, catalog);
        var finish = catalog.Finishes.FirstOrDefault(value =>
            string.Equals(value.Label, DefaultFinish, StringComparison.Ordinal))?.Value;
        var lockValue = system is null
            ? null
            : catalog.Systems.FirstOrDefault(value =>
                string.Equals(value.ExcelValue, system, StringComparison.Ordinal))?.Lock;
        if (string.IsNullOrWhiteSpace(lockValue))
        {
            lockValue = null;
        }

        var glassPrice = ResolveGlassPrice(item);
            var pendingFields = BuildPendingFields(system, glassDescription, finish, lockValue, glassPrice);

            return item with
            {
                System = system,
                GlassDescription = glassDescription,
                Finish = finish,
                Lock = lockValue,
                GlassPrice = glassPrice,
                PendingFields = pendingFields
            };
    }

    private static string? ResolveSystem(
        FpProPreviewItemData item,
        QuotationTemplateCatalog catalog)
    {
        var profiles = item.FpProProfiles
            .Select(NormalizeToken)
            .ToHashSet(StringComparer.Ordinal);

        var target = ResolveSystemDisplayName(item, profiles);
        return target is not null
            ? catalog.Systems.FirstOrDefault(value =>
                string.Equals(value.Label, target, StringComparison.Ordinal))?.ExcelValue
            : null;
    }

    private static string? ResolveSystemDisplayName(
        FpProPreviewItemData item,
        IReadOnlySet<string> profiles)
    {
        if (SetEquals(profiles, "KONCEPT40", "TUBULARES", "ALFAJIA", "VITRINA"))
        {
            return "CUERPO BATIENTE LINEA PREMIUM TIPO EUROPEO VENECIA FERMO";
        }

        if (SetEquals(profiles, "KONCEPT40", "ALFAJIA")
            || SetEquals(profiles, "KONCEPT40", "ALFAJIA", "VITRINA"))
        {
            return "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO";
        }

        if (SetEquals(profiles, "KONCEPT50", "ALFAJIA")
            || SetEquals(profiles, "KONCEPT40", "KONCEPT50", "TUBULARES", "ALFAJIA"))
        {
            return "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA";
        }

        if (SetEquals(profiles, "SUPERIOR50", "ALFAJIA")
            || SetEquals(profiles, "SUPERIOR50", "SERIE35", "TUBULARES", "ALFAJIA", "SUPERIOR33"))
        {
            return "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO";
        }

        if (SetEquals(profiles, "KONCEPT70", "ANGULOS"))
        {
            return ContainsToken(item.Notes, "BOLSILLO")
                ? "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET"
                : "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES";
        }

        if (SetEquals(profiles, "KONCEPT100", "ANGULOS"))
        {
            return "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONACO";
        }

        if (SetEquals(profiles, "3890", "SERIE35", "VITRINA")
            || SetEquals(profiles, "KONCEPT40", "3890", "TUBULARES", "SERIE35", "ALFAJIA")
            || SetEquals(profiles, "3890", "KONCEPT40", "TUBULARES", "SERIE35", "ALFAJIA"))
        {
            return "PUERTA BATIENTE LINEA CLASSIC SISTEMA SERIE SG 3890";
        }

        if (SetEquals(profiles, "TUBULARES"))
        {
            return "SISTEMA SG CLARABOYA";
        }

        if (SetEquals(profiles, "SERIE35", "ALFAJIA"))
        {
            return ResolveSerie35(item.TechnicalProfileDescriptions);
        }

        return null;
    }

    private static string? ResolveSerie35(IReadOnlyList<string> descriptions)
    {
        var hasProjecting = descriptions.Any(value =>
            ContainsToken(value, "MARCO VENTANA PROYECTANTE")
            || ContainsToken(value, "NAVE HORIZ VERT")
            || ContainsToken(value, "NAVE HORIZ/VERT"));
        var hasCasement = descriptions.Any(value =>
            ContainsToken(value, "PISAVIDRIO PUERTA BATIENTE")
            || ContainsToken(value, "NAVE2295")
            || ContainsToken(value, "NAVE CAMARA EUROPEA"));

        return (hasProjecting, hasCasement) switch
        {
            (true, false) => "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA",
            (false, true) => "CUERPO BATIENTE LINEA CLASSIC PRIMAVERA SIENA",
            _ => null
        };
    }

    private static string? ResolveGlassDescription(
        decimal? selectedThicknessMm,
        QuotationTemplateCatalog catalog)
    {
        if (selectedThicknessMm is null)
        {
            return null;
        }

        var expected = $"COMPOSICION MONOLITICO TEMPLADO {selectedThicknessMm.Value:0.##} MM INC";
        return catalog.GlassDescriptions.FirstOrDefault(value =>
            string.Equals(value.Label, expected, StringComparison.Ordinal))?.Value;
    }

    private static decimal? ResolveGlassPrice(FpProPreviewItemData item)
{
    if (item.Glass.Count == 0 || item.Quantity is null or <= 0)
    {
        return null;
    }

    var groups = item.Glass
        .Select(pane => new
        {
            Rate = ResolveGlassRate(pane),
            Area = ResolvePaneAreaM2(pane)
        })
        .ToArray();

    if (groups.Any(value => value.Rate is null || value.Area is null))
    {
        return null;
    }

    var byRate = groups
        .GroupBy(value => value.Rate!.Value)
        .Select(group => new
        {
            Rate = group.Key,
            Area = group.Sum(value => value.Area!.Value)
        })
        .ToArray();

    if (byRate.Length == 1)
    {
        var areaPerStructure = byRate[0].Area / item.Quantity.Value;
        var billableArea = Math.Max(1m, areaPerStructure);
        return Math.Round(billableArea * byRate[0].Rate, 1, MidpointRounding.AwayFromZero);
    }

    if (byRate.Any(value => value.Area / item.Quantity.Value < 1m))
    {
        return null;
    }

    var total = byRate.Sum(value => (value.Area / item.Quantity.Value) * value.Rate);
    return Math.Round(total, 1, MidpointRounding.AwayFromZero);
}

private static decimal? ResolvePaneAreaM2(FpProGlassPaneData pane)
{
    if (pane.WidthMm is null or <= 0 || pane.HeightMm is null or <= 0 || pane.Quantity is null or <= 0)
    {
        return null;
    }

    return (pane.WidthMm.Value / 1000m)
        * (pane.HeightMm.Value / 1000m)
        * pane.Quantity.Value;
}

private static decimal? ResolveGlassRate(FpProGlassPaneData pane)
{
    return NormalizeToken(pane.Code) switch
    {
        "05MM" => 74000m,
        "06MM" => 74000m,
        "08MM" => 90000m,
        "10MM" => 126000m,
        _ => null
    };
}

    private static string[] BuildPendingFields(
    string? system,
    string? glassDescription,
    string? finish,
    string? lockValue,
    decimal? glassPrice)
    {
        var result = new List<string>();
        if (system is null)
        {
            result.Add(SystemField);
        }

        if (glassDescription is null)
        {
            result.Add(GlassDescriptionField);
        }

        if (finish is null)
        {
            result.Add(FinishField);
        }

        if (glassPrice is null)
        {
            result.Add(GlassPriceField);
        }
        if (lockValue is null)
        {
            result.Add(LockField);
        }

        result.Add(ModuleField);
        return result.ToArray();
    }

    private static bool SetEquals(IReadOnlySet<string> values, params string[] expected) =>
        values.SetEquals(expected.Select(NormalizeToken));

    private static bool ContainsToken(string? value, string expected) =>
        NormalizeText(value).Contains(NormalizeText(expected), StringComparison.Ordinal);

    private static string NormalizeToken(string value) => NormalizeText(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
