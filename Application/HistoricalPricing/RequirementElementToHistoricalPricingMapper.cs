using System.Globalization;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Domain.PreQuotes;

namespace Application.HistoricalPricing;

public sealed class RequirementElementToHistoricalPricingMapper
    : IRequirementElementToHistoricalPricingMapper
{
    private const string AreaMismatchCode = "MEASUREMENT_AREA_MISMATCH";

    public RequirementElementHistoricalPricingMapping Map(
        StructuredItemData item,
        IReadOnlyList<ProcessingWarningData> warnings)
    {
        var mappingWarnings = warnings
            .Select(value => value.Code)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddStatusWarning(item.ExtractionStatus, "ITEM", mappingWarnings);

        var category = item.ExtractionStatus == CanonicalExtractionValueStatus.Unknown
            ? null
            : Category(item.ElementType);
        var system = MapTechnicalValue(
            item.TechnicalClassification?.SystemCode,
            item.TechnicalClassification?.SystemSource,
            "SYSTEM",
            mappingWarnings);
        var finish = MapTechnicalValue(
            item.TechnicalClassification?.FinishCode,
            item.TechnicalClassification?.FinishSource,
            "FINISH",
            mappingWarnings);
        var glass = Glass(item.Glass, mappingWarnings);
        var areaMismatch = mappingWarnings.Contains(AreaMismatchCode);
        var requiresReview = item.RequiresReview
            || item.ExtractionStatus is CanonicalExtractionValueStatus.Ambiguous
                or CanonicalExtractionValueStatus.Unknown
            || item.TechnicalClassification?.RequiresReview == true
            || item.Glass?.RequiresReview == true
            || areaMismatch;

        return new RequirementElementHistoricalPricingMapping(
            item.Sequence,
            item.Reference,
            new HistoricalCandidateQuery(
                category,
                system,
                glass.Family,
                glass.Thickness,
                EmptyToNull(item.Configuration),
                item.WidthMillimeters,
                item.HeightMillimeters,
                item.AreaSquareMeters,
                finish,
                item.Quantity,
                GlassComposition: glass.Composition),
            item.ExtractionStatus,
            item.Confidence,
            mappingWarnings.Order(StringComparer.Ordinal).ToArray(),
            requiresReview);
    }

    private static string? Category(StructuredElementType value) => value switch
    {
        StructuredElementType.Window => "VENTANA",
        StructuredElementType.Door => "PUERTA",
        StructuredElementType.Facade => "FACHADA",
        StructuredElementType.Partition => "DIVISION",
        StructuredElementType.Railing => "BARANDA",
        StructuredElementType.Skylight => "LUCERNARIO",
        StructuredElementType.ShowerDivision => "DIVISION_BANO",
        StructuredElementType.Other => null,
        _ => null
    };

    private static string? MapTechnicalValue(
        string? value,
        TechnicalClassificationSource? source,
        string field,
        ISet<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)
            || source is null or TechnicalClassificationSource.Unresolved)
        {
            if (source == TechnicalClassificationSource.Unresolved)
            {
                warnings.Add($"{field}_UNKNOWN");
            }
            return null;
        }
        if (source == TechnicalClassificationSource.Inferred)
        {
            warnings.Add($"{field}_INFERRED");
        }
        return value.Trim();
    }

    private static GlassValues Glass(
        StructuredItemGlassData? glass,
        ISet<string> warnings)
    {
        if (glass is null || string.IsNullOrWhiteSpace(glass.NormalizedCode)
            || glass.NormalizedCode.Equals("UNKNOWN_GLASS", StringComparison.OrdinalIgnoreCase))
        {
            return new GlassValues(null, null, null);
        }
        if (glass.RequiresReview)
        {
            warnings.Add("GLASS_AMBIGUOUS");
        }

        var parts = glass.NormalizedCode.Trim().ToUpperInvariant().Split('_');
        if (parts.Length >= 2 && parts[0] == "TEMP"
            && decimal.TryParse(parts[1], NumberStyles.Number,
                CultureInfo.InvariantCulture, out var temperedThickness))
        {
            return new GlassValues("TEMPLADO", temperedThickness, null);
        }
        if (parts.Length >= 3 && parts[0] == "LAM"
            && decimal.TryParse(parts[1], NumberStyles.Number,
                CultureInfo.InvariantCulture, out var laminatedThickness))
        {
            return new GlassValues(
                "LAMINADO",
                laminatedThickness,
                string.Join('+', parts.Skip(1).Take(2)));
        }

        warnings.Add("GLASS_CANONICAL_CODE_UNMAPPED");
        return new GlassValues(null, null, null);
    }

    private static void AddStatusWarning(
        CanonicalExtractionValueStatus status,
        string field,
        ISet<string> warnings)
    {
        if (status == CanonicalExtractionValueStatus.Inferred)
        {
            warnings.Add($"{field}_INFERRED");
        }
        else if (status == CanonicalExtractionValueStatus.Ambiguous)
        {
            warnings.Add($"{field}_AMBIGUOUS");
        }
        else if (status == CanonicalExtractionValueStatus.Unknown)
        {
            warnings.Add($"{field}_UNKNOWN");
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record GlassValues(
        string? Family,
        decimal? Thickness,
        string? Composition);
}
