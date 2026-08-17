using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.HistoricalPricing;

public static partial class HistoricalQuoteNormalizer
{
    public static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder();
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        }
        return SpacesRegex().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    public static string? InferCategory(string? description, string? location)
    {
        var text = NormalizeText($"{description} {location}") ?? string.Empty;
        if (text.Contains("BARANDA", StringComparison.Ordinal)) return "BARANDA";
        if (text.Contains("LUCERNARIO", StringComparison.Ordinal)) return "LUCERNARIO";
        if (text.Contains("DIVISION", StringComparison.Ordinal) && text.Contains("BANO", StringComparison.Ordinal)) return "DIVISION_BANO";
        if (text.Contains("FACHADA", StringComparison.Ordinal)) return "FACHADA";
        if (text.Contains("PUERTA", StringComparison.Ordinal)) return "PUERTA";
        if (text.Contains("VENTANA", StringComparison.Ordinal)) return "VENTANA";
        return null;
    }

    public static string? GlassFamily(string? value)
    {
        var text = NormalizeText(value) ?? string.Empty;
        if (text.Contains("LAMINADO", StringComparison.Ordinal) && text.Contains("TEMPLADO", StringComparison.Ordinal)) return "LAMINADO_TEMPLADO";
        if (text.Contains("LAMINADO", StringComparison.Ordinal) || text.Contains("PVB", StringComparison.Ordinal)) return "LAMINADO";
        if (text.Contains("TEMPLADO", StringComparison.Ordinal)) return "TEMPLADO";
        if (text.Contains("CAMARA", StringComparison.Ordinal) || text.Contains("IGU", StringComparison.Ordinal)) return "IGU";
        if (text.Contains("MONOLIT", StringComparison.Ordinal) || text.Contains("CRUDO", StringComparison.Ordinal)) return "MONOLITICO";
        return null;
    }

    public static decimal? GlassThickness(string? value)
    {
        var normalized = NormalizeText(value) ?? string.Empty;
        var match = ThicknessRegex().Match(normalized);
        if (!match.Success && (normalized.Contains("PVB", StringComparison.Ordinal) || normalized.Contains('+')))
            match = CompositionThicknessRegex().Match(normalized);
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result : null;
    }

    public static string? GlassComposition(string? value)
    {
        var text = NormalizeText(value);
        return text is not null && (text.Contains("PVB", StringComparison.Ordinal) || text.Contains('+')) ? text : null;
    }

    [GeneratedRegex(@"(?<!\d)(\d{1,2}(?:[.,]\d+)?)\s*MM\b")]
    private static partial Regex ThicknessRegex();
    [GeneratedRegex(@"(?<![\d.,])(\d{1,2}(?:[.,]\d+)?)\s*\+")]
    private static partial Regex CompositionThicknessRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
