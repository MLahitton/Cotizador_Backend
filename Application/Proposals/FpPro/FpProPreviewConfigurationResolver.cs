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

        var pendingFields = BuildPendingFields(system, glassDescription, finish, lockValue);

        return item with
        {
            System = system,
            GlassDescription = glassDescription,
            Finish = finish,
            Lock = lockValue,
            GlassPrice = null,
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

    private static string[] BuildPendingFields(
        string? system,
        string? glassDescription,
        string? finish,
        string? lockValue)
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

        if (lockValue is null)
        {
            result.Add(LockField);
        }

        result.Add(GlassPriceField);
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
