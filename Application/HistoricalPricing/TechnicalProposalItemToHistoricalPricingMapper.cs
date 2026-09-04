using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Domain.PreQuotes;

namespace Application.HistoricalPricing;

public sealed class TechnicalProposalItemToHistoricalPricingMapper
    : ITechnicalProposalItemToHistoricalPricingMapper
{
    public const string AreaDerivedFromGeometryWarning =
        "AREA_DERIVED_FROM_GEOMETRY_DUE_TO_MISMATCH";
    private const decimal AreaTolerance = 0.10m;

    public TechnicalProposalItemHistoricalPricingMapping Map(
        RequirementTechnicalProposalItem proposalItem,
        ProductSystemCatalogReadModel system,
        GlassTypeCatalogReadModel glass,
        FinishTypeCatalogReadModel finish)
    {
        ArgumentNullException.ThrowIfNull(proposalItem);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(glass);
        ArgumentNullException.ThrowIfNull(finish);

        var extracted = proposalItem.ExtractedItem;
        var warnings = new List<string>();
        var area = ResolvePricingArea(proposalItem, warnings);
        var quantity = proposalItem.EffectiveQuantity is > 0
            ? proposalItem.EffectiveQuantity.Value
            : 0m;
        if (quantity <= 0)
        {
            warnings.Add("QUANTITY_MISSING");
        }

        var query = new HistoricalCandidateQuery(
            Category(proposalItem.ElementType),
            SystemValue(system),
            GlassValue(glass),
            GlassThickness(glass) ?? extracted?.GlassThicknessMm,
            extracted?.Arrangement ?? extracted?.Operation ?? extracted?.FunctionalType,
            proposalItem.EffectiveWidthMillimeters,
            proposalItem.EffectiveHeightMillimeters,
            area,
            FinishValue(finish),
            quantity > 0 ? quantity : null,
            10,
            GlassComposition: glass.Composition ?? extracted?.GlassComposition);

        return new TechnicalProposalItemHistoricalPricingMapping(
            query,
            quantity,
            area,
            warnings.Count > 0,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static decimal? ResolvePricingArea(
        RequirementTechnicalProposalItem proposalItem,
        ICollection<string> warnings)
    {
        var extracted = proposalItem.ExtractedItem;
        var geometry = GeometryArea(
            proposalItem.EffectiveWidthMillimeters,
            proposalItem.EffectiveHeightMillimeters);
        var reported = proposalItem.Source == TechnicalProposalItemSource.AiExtracted
            ? extracted?.AreaSquareMeters
            : null;
        if (geometry is > 0 && reported is > 0)
        {
            var difference = Math.Abs(geometry.Value - reported.Value)
                / Math.Max(Math.Abs(geometry.Value), 0.0001m);
            if (difference > AreaTolerance)
            {
                warnings.Add(AreaDerivedFromGeometryWarning);
                return geometry;
            }

            return reported;
        }

        return reported is > 0 ? reported : geometry;
    }

    private static decimal? GeometryArea(int? widthMm, int? heightMm) =>
        widthMm is > 0 && heightMm is > 0
            ? widthMm.Value * heightMm.Value / 1_000_000m
            : null;

    private static string? Category(StructuredElementType value) => value switch
    {
        StructuredElementType.Window => "VENTANA",
        StructuredElementType.Door => "PUERTA",
        StructuredElementType.Facade => "FACHADA",
        StructuredElementType.Partition => "DIVISION",
        StructuredElementType.Railing => "BARANDA",
        StructuredElementType.Skylight => "LUCERNARIO",
        StructuredElementType.ShowerDivision => "DIVISION_BANO",
        _ => null
    };

    private static string SystemValue(ProductSystemCatalogReadModel system) =>
        FirstNonEmpty(system.TechnicalName, system.Name, system.Family, system.Code)!;

    private static string GlassValue(GlassTypeCatalogReadModel glass) =>
        FirstNonEmpty(glass.Name, glass.Family, glass.Code)!;

    private static string FinishValue(FinishTypeCatalogReadModel finish) =>
        FirstNonEmpty(finish.Name, finish.Color, finish.Code)!;

    private static decimal? GlassThickness(GlassTypeCatalogReadModel glass) =>
        glass.OuterThicknessMm ?? glass.InnerThicknessMm;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
