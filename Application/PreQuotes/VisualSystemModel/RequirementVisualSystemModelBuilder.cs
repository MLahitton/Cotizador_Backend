using Application.PreQuotes.GetRequirementTechnicalProposal;
using Domain.PreQuotes;

namespace Application.PreQuotes.VisualSystemModel;

public static class RequirementVisualSystemModelReviewReasons
{
    public const string PanelLayoutUnresolved =
        "VISUAL_PANEL_LAYOUT_UNRESOLVED";
    public const string PanelOrderUnresolved =
        "VISUAL_PANEL_ORDER_UNRESOLVED";
    public const string CornerTopologyUnresolved =
        "VISUAL_CORNER_TOPOLOGY_UNRESOLVED";
    public const string SystemUnresolved =
        "VISUAL_SYSTEM_UNRESOLVED";
    public const string SubPanelLayoutUnresolved =
        "VISUAL_SUBPANEL_LAYOUT_UNRESOLVED";
    public const string SubPanelOrderUnresolved =
        "VISUAL_SUBPANEL_ORDER_UNRESOLVED";
}

public static class RequirementVisualSystemModelBuilder
{
    private const string Version = "1.0";
    private const string SelectedSystemSource = "SELECTED_SYSTEM";
    private const string SuggestedSystemSource = "SUGGESTED_SYSTEM";
    private const string ExtractionOnlySource = "EXTRACTION_ONLY";

    public static RequirementTechnicalProposalVisualModelReadModel Build(
        RequirementTechnicalProposalItem item,
        RequirementTechnicalProposalSystemOptionReadModel? suggestedSystem,
        RequirementTechnicalProposalSelectedReadModel? selected)
    {
        var extracted = item.ExtractedItem;
        var system = selected?.System ?? suggestedSystem;
        var source = selected?.System is not null
            ? SelectedSystemSource
            : suggestedSystem is not null
                ? SuggestedSystemSource
                : ExtractionOnlySource;
        var reviewReasons = new List<string>();
        var panels = BuildPanels(item, extracted, reviewReasons);
        var geometryType = extracted?.GeometryType;

        if (system is null)
        {
            reviewReasons.Add(
                RequirementVisualSystemModelReviewReasons.SystemUnresolved);
        }

        if (string.Equals(
                geometryType,
                "CORNER",
                StringComparison.OrdinalIgnoreCase))
        {
            reviewReasons.Add(
                RequirementVisualSystemModelReviewReasons.CornerTopologyUnresolved);
        }

        return new RequirementTechnicalProposalVisualModelReadModel(
            Version,
            source,
            MapSystem(system),
            extracted?.FunctionalType,
            extracted?.Operation,
            geometryType,
            item.EffectiveWidthMillimeters,
            item.EffectiveHeightMillimeters,
            item.EffectiveQuantity,
            panels,
            [],
            extracted?.SpecialFeatures ?? [],
            reviewReasons.Count > 0,
            reviewReasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<RequirementTechnicalProposalVisualPanelReadModel>
        BuildPanels(
            RequirementTechnicalProposalItem item,
            RequirementExtractedItem? extracted,
            List<string> reviewReasons)
    {
        if (extracted is null)
        {
            reviewReasons.Add(
                RequirementVisualSystemModelReviewReasons.PanelLayoutUnresolved);
            return [];
        }

        if (extracted.Segments.Count > 0 && HasExplicitVisualSegments(extracted))
        {
            return extracted.Segments
                .OrderBy(segment => segment.Sequence)
                .ThenBy(segment => segment.Id)
                .Select((segment, index) => MapSegment(
                    segment,
                    index + 1,
                    item.EffectiveWidthMillimeters,
                    item.EffectiveHeightMillimeters,
                    extracted.Operation,
                    extracted.OpeningDirection,
                    reviewReasons))
                .ToArray();
        }

        if (extracted.PanelCount is > 0)
        {
            reviewReasons.Add(
                RequirementVisualSystemModelReviewReasons.PanelOrderUnresolved);
            return BuildPanelsFromCounts(extracted).ToArray();
        }

        if (extracted.Segments.Count > 0)
        {
            return extracted.Segments
                .OrderBy(segment => segment.Sequence)
                .ThenBy(segment => segment.Id)
                .Select((segment, index) => MapSegment(
                    segment,
                    index + 1,
                    item.EffectiveWidthMillimeters,
                    item.EffectiveHeightMillimeters,
                    extracted.Operation,
                    extracted.OpeningDirection,
                    reviewReasons))
                .ToArray();
        }

        reviewReasons.Add(
            RequirementVisualSystemModelReviewReasons.PanelLayoutUnresolved);
        return [];
    }

    private static bool HasExplicitVisualSegments(
        RequirementExtractedItem extracted)
    {
        return extracted.Segments.Any(
            segment => !IsOperationOnlySegment(segment, extracted));
    }

    private static bool IsOperationOnlySegment(
        RequirementExtractedItemSegment segment,
        RequirementExtractedItem extracted)
    {
        var segmentRole = NormalizeRole(segment.Role, segment.Operation);
        var itemOperation = NormalizeRole(extracted.Operation, extracted.FunctionalType);

        return segmentRole == itemOperation
            && segmentRole == "SLIDING"
            && segment.Quantity is null
            && segment.WidthMillimeters is null
            && segment.HeightMillimeters is null;
    }

    private static IEnumerable<RequirementTechnicalProposalVisualPanelReadModel>
        BuildPanelsFromCounts(RequirementExtractedItem extracted)
    {
        var panelCount = extracted.PanelCount!.Value;
        var fixedCount = Math.Max(extracted.FixedPanelCount ?? 0, 0);
        var movableCount = Math.Max(extracted.MovablePanelCount ?? 0, 0);
        var assignedCount = Math.Min(fixedCount + movableCount, panelCount);
        var unknownCount = panelCount - assignedCount;
        var index = 1;
        var movableRole = NormalizeRole(
            extracted.Operation,
            extracted.FunctionalType);

        if (movableRole is "FIXED" or "UNKNOWN")
        {
            movableRole = "UNKNOWN";
        }

        for (var count = 0; count < fixedCount && index <= panelCount; count++)
        {
            yield return Panel(index++, "FIXED", null, null, null, null,
                null, false, null, null, []);
        }

        for (var count = 0; count < movableCount && index <= panelCount; count++)
        {
            yield return Panel(index++, movableRole, extracted.Operation, null,
                null, null, null, IsMovable(movableRole),
                extracted.OpeningDirection, extracted.Confidence, []);
        }

        for (var count = 0; count < unknownCount && index <= panelCount; count++)
        {
            yield return Panel(index++, "UNKNOWN", extracted.Operation, null,
                null, null, null, null, extracted.OpeningDirection,
                extracted.Confidence, []);
        }
    }

    private static RequirementTechnicalProposalVisualPanelReadModel MapSegment(
        RequirementExtractedItemSegment segment,
        int index,
        int? totalWidthMm,
        int? totalHeightMm,
        string? itemOperation,
        string? itemOpeningDirection,
        List<string> reviewReasons)
    {
        var role = NormalizeRole(segment.Role, segment.Operation);
        var subPanels = TryBuildSubPanels(segment, itemOpeningDirection);

        if (IsCompositeDeclaration(segment) && subPanels.Count == 0)
        {
            reviewReasons.Add(
                RequirementVisualSystemModelReviewReasons
                    .SubPanelLayoutUnresolved);
        }

        if (subPanels.Count > 0)
        {
            var compositeRole = subPanels
                .FirstOrDefault(panel => panel.IsMovable == true)
                ?.Role ?? role;

            return new RequirementTechnicalProposalVisualPanelReadModel(
                index,
                "COMPOSITE",
                compositeRole,
                segment.Operation ?? itemOperation,
                segment.WidthMillimeters,
                segment.HeightMillimeters,
                Ratio(segment.WidthMillimeters, totalWidthMm),
                Ratio(segment.HeightMillimeters, totalHeightMm),
                subPanels.Any(panel => panel.IsMovable == true),
                null,
                segment.Confidence,
                subPanels);
        }

        return Panel(
            index,
            role,
            segment.Operation ?? itemOperation,
            segment.WidthMillimeters,
            segment.HeightMillimeters,
            Ratio(segment.WidthMillimeters, totalWidthMm),
            Ratio(segment.HeightMillimeters, totalHeightMm),
            IsMovable(role),
            itemOpeningDirection,
            segment.Confidence,
            []);
    }

    private static RequirementTechnicalProposalVisualPanelReadModel Panel(
        int index,
        string role,
        string? operation,
        int? widthMm,
        int? heightMm,
        decimal? widthRatio,
        decimal? heightRatio,
        bool? isMovable,
        string? openingDirection,
        decimal? confidence,
        IReadOnlyList<RequirementTechnicalProposalVisualPanelReadModel>
            subPanels) =>
        new(index, "SIMPLE", role, operation, widthMm, heightMm, widthRatio,
            heightRatio, isMovable, openingDirection, confidence, subPanels);

    private static IReadOnlyList<RequirementTechnicalProposalVisualPanelReadModel>
        TryBuildSubPanels(
            RequirementExtractedItemSegment segment,
            string? itemOpeningDirection)
    {
        if (!IsCompositeDeclaration(segment)
            || string.IsNullOrWhiteSpace(segment.Operation))
        {
            return [];
        }

        var children = segment.Operation
            .Split(['+', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select((token, index) => MapSubPanel(
                token,
                index + 1,
                segment.HeightMillimeters,
                itemOpeningDirection,
                segment.Confidence))
            .Where(panel => panel is not null)
            .Select(panel => panel!)
            .ToArray();

        return children.Length > 0 ? children : [];
    }

    private static RequirementTechnicalProposalVisualPanelReadModel? MapSubPanel(
        string token,
        int index,
        int? parentHeightMm,
        string? itemOpeningDirection,
        decimal? confidence)
    {
        var parts = token.Split(':', StringSplitOptions.TrimEntries);
        var role = NormalizeRole(parts.FirstOrDefault());
        if (role == "UNKNOWN")
        {
            return null;
        }

        var heightMm = parts.Length > 1
            && int.TryParse(parts[1], out var parsedHeight)
                ? parsedHeight
                : (int?)null;
        var isMovable = IsMovable(role);

        return Panel(
            index,
            role,
            isMovable == true ? role : null,
            null,
            heightMm,
            null,
            Ratio(heightMm, parentHeightMm),
            isMovable,
            isMovable == true ? itemOpeningDirection : null,
            confidence,
            []);
    }

    private static bool IsCompositeDeclaration(
        RequirementExtractedItemSegment segment)
    {
        var role = Code(segment.Role);
        return role is not null
            && (role == "COMPOSITE" || role == "MODULE"
                || role.StartsWith("COMPOSITE_", StringComparison.Ordinal));
    }

    private static RequirementTechnicalProposalVisualSystemReadModel? MapSystem(
        RequirementTechnicalProposalSystemOptionReadModel? system) =>
        system is null ? null : new(system.Id, system.Code, system.DisplayName);

    private static decimal? Ratio(int? value, int? total) =>
        value is > 0 && total is > 0 && value <= total
            ? decimal.Divide(value.Value, total.Value)
            : null;

    private static bool? IsMovable(string role) =>
        role switch
        {
            "FIXED" => false,
            "PROJECTING" or "SLIDING" or "HINGED" or "FOLDING" => true,
            _ => null
        };

    private static string NormalizeRole(params string?[] values)
    {
        foreach (var value in values)
        {
            var code = Code(value);
            if (code is null)
            {
                continue;
            }

            if (code.Contains("FIXED", StringComparison.Ordinal)
                || code.Contains("FIJO", StringComparison.Ordinal))
            {
                return "FIXED";
            }

            if (code.Contains("PROJECTING", StringComparison.Ordinal)
                || code.Contains("PROYECTANTE", StringComparison.Ordinal))
            {
                return "PROJECTING";
            }

            if (code.Contains("SLIDING", StringComparison.Ordinal)
                || code.Contains("CORRED", StringComparison.Ordinal))
            {
                return "SLIDING";
            }

            if (code.Contains("HINGED", StringComparison.Ordinal)
                || code.Contains("SWING", StringComparison.Ordinal)
                || code.Contains("CASEMENT", StringComparison.Ordinal)
                || code.Contains("BATIENTE", StringComparison.Ordinal))
            {
                return "HINGED";
            }

            if (code.Contains("FOLDING", StringComparison.Ordinal)
                || code.Contains("PLEGABLE", StringComparison.Ordinal))
            {
                return "FOLDING";
            }

            if (code.Contains("LOUVER", StringComparison.Ordinal)
                || code.Contains("PERSIANA", StringComparison.Ordinal))
            {
                return "LOUVER";
            }
        }

        return "UNKNOWN";
    }

    private static string? Code(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()
                .ToUpperInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');
}
