using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public static class RequirementFunctionalSelectionContextResolver
{
    public static FunctionalSelectionContext Resolve(
        RequirementExtractedItem item,
        string? requestedCommercialLine = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var primary = ResolvePrimaryComponent(item);
        var functionalType = primary.OverridesItem
            ? primary.FunctionalType
            : item.FunctionalType;
        var operation = primary.OverridesItem
            ? primary.Operation
            : item.Operation;

        var geometry = ResolvePrimaryComponentGeometry(item, primary);
        var widthMillimeters = geometry.WidthMillimeters ?? item.WidthMillimeters;
        var heightMillimeters = geometry.HeightMillimeters ?? item.HeightMillimeters;
        var reviewReasons = new List<string>();
        if (geometry.IsUnresolved
            && IsDimensionDependentFunctionalClassification(functionalType, operation))
        {
            widthMillimeters = null;
            heightMillimeters = null;
            reviewReasons.Add(SgTechnicalSelectionReviewReasons.PrimaryComponentGeometryUnresolved);
        }

        var input = new SgTechnicalSelectionInput(
            functionalType,
            operation,
            widthMillimeters,
            heightMillimeters,
            item.AreaSquareMeters,
            item.PanelCount,
            item.MovablePanelCount,
            item.FixedPanelCount,
            item.Modulation,
            item.OpeningDirection,
            item.SpecialFeatures,
            item.GeometryType,
            requestedCommercialLine,
            item.RequestedSystemRaw ?? item.RequestedProfileRaw,
            item.Arrangement,
            null,
            item.Description,
            primary.OverridesItem && !geometry.IsUnresolved
                ? geometry.HeightMillimeters
                : null,
            item.Segments.Count > 1 || geometry.IsUnresolved,
            AssociatedSystemContextTexts(item));

        return new(input, primary, geometry, reviewReasons);
    }

    public static SgTechnicalSelectionInput ResolveInput(
        RequirementTechnicalProposalItem item,
        string? requestedCommercialLine = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ExtractedItem is { } extracted)
        {
            var input = Resolve(extracted, requestedCommercialLine).Input;
            return input with
            {
                WidthMillimeters = item.ManualWidthMillimetersOverride
                    ?? input.WidthMillimeters,
                HeightMillimeters = item.ManualHeightMillimetersOverride
                    ?? input.HeightMillimeters,
                RequestedCommercialLine = requestedCommercialLine
            };
        }

        return new SgTechnicalSelectionInput(
            SgFunctionalCompatibilityEvaluator.FunctionalTypeFromElementType(
                item.ElementType),
            null,
            item.EffectiveWidthMillimeters,
            item.EffectiveHeightMillimeters,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            requestedCommercialLine,
            null,
            null,
            null,
            item.Description,
            null,
            false,
            null);
    }

    private static IReadOnlyList<string> AssociatedSystemContextTexts(
        RequirementExtractedItem item)
    {
        var values = new List<string?>
        {
            item.Description,
            item.FunctionalType,
            item.Operation,
            item.Arrangement,
            item.Modulation,
            item.OpeningDirection,
            item.GeometryType,
            item.AssemblyType,
            item.RequestedSystemRaw,
            item.RequestedProfileRaw
        };

        values.AddRange(item.SpecialFeatures);
        values.AddRange(item.Evidence.Select(evidence => evidence.Text));
        values.AddRange(item.Segments.Select(segment => segment.EvidenceText));
        values.AddRange(item.Segments.Select(segment => segment.Role));
        values.AddRange(item.Segments.Select(segment => segment.Operation));
        values.AddRange(item.Segments.Select(segment => segment.GeometryType));

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssemblyPrimaryComponentResolution ResolvePrimaryComponent(
        RequirementExtractedItem item)
    {
        var roles = item.Segments
            .Select(segment => ComponentRole(segment.Role ?? segment.Operation))
            .Where(role => role is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0)
        {
            return AssemblyPrimaryComponentResolution.NoOverride();
        }

        var movable = roles
            .Select(role => MovableFunctionalType(role, item))
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (movable.Length == 1)
        {
            var primaryRole = roles.First(role =>
                MovableFunctionalType(role, item) == movable[0]);
            return AssemblyPrimaryComponentResolution.Override(
                movable[0],
                primaryRole,
                OperationFromFunctionalType(movable[0]));
        }

        if (movable.Length > 1)
        {
            return AssemblyPrimaryComponentResolution.RequiresReview(
                [SgTechnicalSelectionReviewReasons
                    .AssemblyMultipleMovableTypesRequiresReview]);
        }

        var specialDomain = PreservedSpecialFunctionalDomain(item);
        if (specialDomain is not null)
        {
            return AssemblyPrimaryComponentResolution.Override(
                specialDomain,
                null,
                item.Operation);
        }

        if (roles.Contains("FIXED", StringComparer.Ordinal))
        {
            return AssemblyPrimaryComponentResolution.Override("FIXED", "FIXED", "FIXED");
        }

        if (roles.Any(role => role is "GRILLE" or "LOUVER"))
        {
            return AssemblyPrimaryComponentResolution.Override("GRILLE", "GRILLE", null);
        }

        return AssemblyPrimaryComponentResolution.RequiresReview(
            [SgTechnicalSelectionReviewReasons
                .TechnicalSelectionCatalogMetadataIncomplete]);
    }

    private static string? PreservedFunctionalType(
        RequirementExtractedItem item,
        string primaryRole)
    {
        var functionalType = NormalizedFunctionalType(item.FunctionalType);
        if (functionalType is null)
        {
            return null;
        }

        if (IsSpecialFunctionalDomain(functionalType))
        {
            return functionalType;
        }

        return primaryRole switch
        {
            "SLIDING" when functionalType is "SLIDING_WINDOW" or "SLIDING_DOOR" =>
                functionalType,
            "PROJECTING" when functionalType == "PROJECTING" => functionalType,
            "SWING" when functionalType == "SWING_DOOR" => functionalType,
            "CASEMENT" when functionalType == "CASEMENT" => functionalType,
            "FOLDING" when functionalType is "FOLDING_WINDOW" or "FOLDING_DOOR" =>
                functionalType,
            "FIXED" when functionalType == "FIXED" => functionalType,
            "GRILLE" when functionalType is "GRILLE" or "LOUVER" => functionalType,
            _ => null
        };
    }

    private static bool IsSpecialFunctionalDomain(string functionalType) =>
        functionalType is "SHOWER_DIVISION"
            or "SKYLIGHT"
            or "RAILING"
            or "FACADE";

    private static string? PreservedSpecialFunctionalDomain(
        RequirementExtractedItem item)
    {
        var functionalType = NormalizedFunctionalType(item.FunctionalType);
        return functionalType is not null && IsSpecialFunctionalDomain(functionalType)
            ? functionalType
            : null;
    }

    private static string? ComponentRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized switch
        {
            "SLIDING" or "SLIDING_DOOR" or "SLIDING_WINDOW" => "SLIDING",
            "PROJECTING" => "PROJECTING",
            "SWING" or "SWING_DOOR" => "SWING",
            "CASEMENT" => "CASEMENT",
            "FOLDING" or "FOLDING_DOOR" or "FOLDING_WINDOW" => "FOLDING",
            "FIXED" => "FIXED",
            "GRILLE" or "LOUVER" => "GRILLE",
            _ => null
        };
    }

    private static string? MovableFunctionalType(
        string role,
        RequirementExtractedItem item) =>
        role switch
        {
            "SLIDING" => PreservedFunctionalType(item, role)
                ?? (item.ElementType == StructuredElementType.Window
                ? "SLIDING_WINDOW"
                : "SLIDING_DOOR"),
            "PROJECTING" => PreservedFunctionalType(item, role) ?? "PROJECTING",
            "SWING" => PreservedFunctionalType(item, role) ?? "SWING_DOOR",
            "CASEMENT" => PreservedFunctionalType(item, role) ?? "CASEMENT",
            "FOLDING" => PreservedFunctionalType(item, role)
                ?? (item.ElementType == StructuredElementType.Window
                ? "FOLDING_WINDOW"
                : "FOLDING_DOOR"),
            _ => null
        };

    private static string? OperationFromFunctionalType(string functionalType) =>
        functionalType switch
        {
            "SLIDING_DOOR" or "SLIDING_WINDOW" => "SLIDING",
            "PROJECTING" => "PROJECTING",
            "SWING_DOOR" => "SWING",
            "CASEMENT" => "CASEMENT",
            "FOLDING_DOOR" or "FOLDING_WINDOW" => "FOLDING",
            _ => null
        };

    private static PrimaryComponentGeometryResolution ResolvePrimaryComponentGeometry(
        RequirementExtractedItem item,
        AssemblyPrimaryComponentResolution primary)
    {
        if (!primary.OverridesItem || primary.PrimaryRole is null)
        {
            return PrimaryComponentGeometryResolution.FromElement(
                item.WidthMillimeters,
                item.HeightMillimeters);
        }

        var matchingSegments = item.Segments
            .Where(segment => ComponentRole(segment.Role ?? segment.Operation) == primary.PrimaryRole)
            .OrderBy(segment => segment.Sequence)
            .ToArray();

        var explicitGeometry = matchingSegments
            .Where(segment => segment.WidthMillimeters is > 0
                && segment.HeightMillimeters is > 0)
            .OrderByDescending(segment => segment.HeightMillimeters)
            .ThenByDescending(segment => segment.WidthMillimeters)
            .FirstOrDefault();

        if (explicitGeometry is not null)
        {
            return PrimaryComponentGeometryResolution.FromComponent(
                explicitGeometry.WidthMillimeters,
                explicitGeometry.HeightMillimeters);
        }

        if (item.Segments.Count == 0
            || (item.Segments.Count == 1 && matchingSegments.Length == 1))
        {
            return PrimaryComponentGeometryResolution.FromElement(
                item.WidthMillimeters,
                item.HeightMillimeters);
        }

        return PrimaryComponentGeometryResolution.Unresolved();
    }

    private static bool IsDimensionDependentFunctionalClassification(
        string? functionalType,
        string? operation)
    {
        var normalizedFunctionalType = NormalizedFunctionalType(functionalType);
        var normalizedOperation = NormalizedFunctionalType(operation);

        return normalizedFunctionalType is "WINDOW" or "SLIDING_WINDOW"
            || normalizedOperation is "SLIDING";
    }

    private static string? NormalizedFunctionalType(string? value) =>
        SgFunctionalCompatibilityEvaluator.Code(value);
}

public sealed record FunctionalSelectionContext(
    SgTechnicalSelectionInput Input,
    AssemblyPrimaryComponentResolution PrimaryComponent,
    PrimaryComponentGeometryResolution Geometry,
    IReadOnlyList<string> ReviewReasons);

public sealed record AssemblyPrimaryComponentResolution(
    bool OverridesItem,
    string? FunctionalType,
    string? PrimaryRole,
    string? Operation,
    IReadOnlyList<string> ReviewReasons)
{
    public static AssemblyPrimaryComponentResolution NoOverride() =>
        new(false, null, null, null, []);

    public static AssemblyPrimaryComponentResolution Override(
        string functionalType,
        string? primaryRole,
        string? operation) =>
        new(true, functionalType, primaryRole, operation, []);

    public static AssemblyPrimaryComponentResolution RequiresReview(
        IReadOnlyList<string> reviewReasons) =>
        new(false, null, null, null, reviewReasons);
}

public sealed record PrimaryComponentGeometryResolution(
    int? WidthMillimeters,
    int? HeightMillimeters,
    bool IsUnresolved)
{
    public static PrimaryComponentGeometryResolution FromElement(
        int? widthMillimeters,
        int? heightMillimeters) =>
        new(widthMillimeters, heightMillimeters, false);

    public static PrimaryComponentGeometryResolution FromComponent(
        int? widthMillimeters,
        int? heightMillimeters) =>
        new(widthMillimeters, heightMillimeters, false);

    public static PrimaryComponentGeometryResolution Unresolved() =>
        new(null, null, true);
}
