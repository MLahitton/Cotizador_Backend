using Application.Common.Abstractions.Catalogs;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public sealed record RequirementGlassDimensions(int? WidthMm, int? HeightMm);

public static class RequirementGlassTechnicalResolver
{
    private const int GlassPaneSegmentationToleranceMillimeters = 10;

    public static GlassCandidateResolutionResult? ResolveByFamily(
        RequirementExtractedItem item,
        string? requiredGlassFamily,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        RequirementGlassDimensions? dimensions = null)
    {
        if (IsExplicitSpecialGlassVariant(item))
        {
            return null;
        }

        return Normalize(requiredGlassFamily) switch
        {
            "TEMPLADO" => ResolveTemperedWithSpecialRules(item, glasses, dimensions),
            "LAMINADO" => ResolveLaminated(item, glasses, dimensions),
            _ => null
        };
    }

    public static GlassCandidateResolutionResult? ResolveTemperedWithSpecialRules(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        RequirementGlassDimensions? dimensions = null)
    {
        if (IsShowerDivision(item))
        {
            return RuleGlass(
                glasses,
                "TEMP_8",
                [GlassResolutionReasonCodes.SpecialGlassShower8Mm]);
        }

        if (IsRailingOrGuardrail(item))
        {
            return RuleGlass(
                glasses,
                "TEMP_10",
                [GlassResolutionReasonCodes.SpecialGlassRailing10Mm]);
        }

        return ResolveTempered(item, glasses, dimensions);
    }

    public static GlassCandidateResolutionResult? ResolveTempered(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        RequirementGlassDimensions? dimensions = null)
    {
        var geometry = GlassPaneGeometry.Resolve(item, dimensions);
        if (geometry is null)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineTempered
        };
        reasons.AddRange(geometry.ResolutionReasons);
        var reviewReasons = new List<string>();
        if (geometry.RequiresReview)
        {
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneGeometryUnresolved);
        }

        var paneCodes = new List<string>();
        var hasJoint = false;
        foreach (var pane in geometry.Panes)
        {
            if (geometry.CanEvaluateJoint && pane.WidthMm > 1950)
            {
                hasJoint = true;
                paneCodes.Add("TEMP_10");
                continue;
            }

            var narrow = pane.WidthMm <= 500;
            paneCodes.Add(pane.HeightMm switch
            {
                <= 2400 => "TEMP_5",
                <= 2600 when narrow => "TEMP_5",
                <= 2600 => "TEMP_6",
                <= 2800 when narrow => "TEMP_6",
                <= 2800 => "TEMP_8",
                <= 3000 when narrow => "TEMP_8",
                _ => "TEMP_10"
            });

            if (narrow && pane.HeightMm > 2400 && pane.HeightMm <= 3000)
            {
                reasons.Add(GlassResolutionReasonCodes
                    .NarrowGlassHeightExtension);
            }
        }

        if (hasJoint)
        {
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
        }

        var code = paneCodes
            .OrderByDescending(TemperedRank)
            .First();
        if (paneCodes.Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            reasons.Add(GlassResolutionReasonCodes
                .GlassPaneHeterogeneousNeeds);
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneHeterogeneousNeeds);
        }

        return RuleGlass(glasses, code, reasons, reviewReasons);
    }

    public static GlassCandidateResolutionResult? ResolveLaminated(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        RequirementGlassDimensions? dimensions = null)
    {
        var geometry = GlassPaneGeometry.Resolve(item, dimensions);
        if (geometry is null)
        {
            return null;
        }

        var reasons = new List<string>
        {
            GlassResolutionReasonCodes.GlassLineLaminated
        };
        reasons.AddRange(geometry.ResolutionReasons);
        var reviewReasons = new List<string>();
        if (geometry.RequiresReview)
        {
            reviewReasons.Add(GlassResolutionReasonCodes
                .GlassPaneGeometryUnresolved);
        }

        var code = "LAM_4_4";
        if (geometry.CanEvaluateJoint
            && geometry.Panes.Any(pane => pane.WidthMm > 1950
                && pane.HeightMm > 2800))
        {
            code = "LAM_5_5";
            reasons.Add(GlassResolutionReasonCodes.JointGlassRule);
            reasons.Add(GlassResolutionReasonCodes.Laminated55JointAndHeight);
        }

        return RuleGlass(glasses, code, reasons, reviewReasons);
    }

    private static GlassCandidateResolutionResult? RuleGlass(
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        string code,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string>? reviewReasons = null)
    {
        var glass = glasses.FirstOrDefault(value =>
            value.IsActive
            && value.IsSelectable
            && value.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (glass is null)
        {
            return null;
        }

        var alternative = new GlassCandidateAlternative(
            glass.GlassTypeId,
            glass.Code,
            glass.Name,
            1m,
            reasons);
        return new GlassCandidateResolutionResult(
            alternative,
            [alternative],
            1m,
            reviewReasons?.Count > 0,
            reviewReasons ?? [],
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static int TemperedRank(string code) => code switch
    {
        "TEMP_10" => 4,
        "TEMP_8" => 3,
        "TEMP_6" => 2,
        _ => 1
    };

    private sealed record GlassPane(int WidthMm, int HeightMm);

    private sealed record GlassPaneGeometry(
        IReadOnlyList<GlassPane> Panes,
        IReadOnlyList<string> ResolutionReasons,
        bool RequiresReview,
        bool CanEvaluateJoint)
    {
        public static GlassPaneGeometry? Resolve(
            RequirementExtractedItem item,
            RequirementGlassDimensions? dimensions)
        {
            var isRoofGlass = IsRoofGlassDomain(item);
            var isPocket = IsPocketDomain(item);
            var effectiveWidth = dimensions is null ? item.WidthMillimeters : dimensions.WidthMm;
            var effectiveHeight = dimensions is null ? item.HeightMillimeters : dimensions.HeightMm;
            if (effectiveWidth is not { } width
                || effectiveHeight is not { } height)
            {
                return TryResolveExplicitSegments(item)
                    ?? TryResolvePocketLeafGeometry(
                        item,
                        effectiveHeight)
                    ?? TryResolveRoofGeometry(item);
            }

            var explicitSegments = TryResolveExplicitSegments(item);
            if (explicitSegments is not null)
            {
                return explicitSegments;
            }

            var explicitPanePairs = TryResolveExplicitPanePairs(item);
            if (explicitPanePairs is not null)
            {
                return explicitPanePairs;
            }

            var explicitHeights = TryResolveExplicitHorizontalPanes(
                item,
                width,
                height);
            if (explicitHeights is not null)
            {
                return explicitHeights;
            }

            var explicitWidths = TryResolveExplicitVerticalPanes(
                item,
                width,
                height);
            if (explicitWidths is not null)
            {
                return explicitWidths;
            }

            var pocketLeafGeometry = TryResolvePocketLeafGeometry(
                item,
                height);
            if (pocketLeafGeometry is not null)
            {
                return pocketLeafGeometry;
            }

            var evidencePanes = TryResolveEvidencePanes(
                item,
                width,
                height);
            if (evidencePanes is not null)
            {
                return evidencePanes;
            }

            var roofGeometry = TryResolveRoofGeometry(item);
            if (roofGeometry is not null)
            {
                return roofGeometry;
            }

            if (isRoofGlass)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                    true,
                    false);
            }

            if (isPocket)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                    true,
                    false);
            }

            if (item.PanelCount == 1)
            {
                return new GlassPaneGeometry(
                    [new GlassPane(width, height)],
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromElement],
                    false,
                    true);
            }

            return new GlassPaneGeometry(
                [new GlassPane(width, height)],
                [GlassResolutionReasonCodes.GlassPaneGeometryUnresolved],
                item.PanelCount is > 1 || width > 1950,
                false);
        }

        private static GlassPaneGeometry? TryResolveRoofGeometry(
            RequirementExtractedItem item)
        {
            if (!IsRoofGlassDomain(item)
                || item.PanelCount is > 1)
            {
                return null;
            }

            foreach (var text in AssociatedEvidenceTexts(item))
            {
                if (TryExtractRoofLengthWidth(text) is not { } dimensions)
                {
                    continue;
                }

                var paneWidth = Math.Min(
                    dimensions.LengthMm,
                    dimensions.WidthMm);
                var paneHeight = Math.Max(
                    dimensions.LengthMm,
                    dimensions.WidthMm);
                return new GlassPaneGeometry(
                    [new GlassPane(paneWidth, paneHeight)],
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromRoofGeometry],
                    false,
                    true);
            }

            return null;
        }

        private static GlassPaneGeometry? TryResolvePocketLeafGeometry(
            RequirementExtractedItem item,
            int? height)
        {
            if (!IsPocketDomain(item)
                || height is not { } paneHeight)
            {
                return null;
            }

            foreach (var text in AssociatedEvidenceTexts(item))
            {
                var repeatedLeafPanes = TryExtractRepeatedPocketLeafPanes(
                    text,
                    paneHeight);
                if (repeatedLeafPanes is not null)
                {
                    return repeatedLeafPanes;
                }

                if (TryExtractPocketLeafWidth(text) is not { } leafWidth)
                {
                    continue;
                }

                return new GlassPaneGeometry(
                    [new GlassPane(leafWidth, paneHeight)],
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromPocketLeaf],
                    false,
                    true);
            }

            return null;
        }

        private static GlassPaneGeometry? TryExtractRepeatedPocketLeafPanes(
            string text,
            int height)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\b(?<count>\d{1,2})\s*(?:hojas|leafs|leaves|naves|panel(?:es)?\s+m[oó]vil(?:es)?)\s*(?:de|=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var count = int.Parse(
                match.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (count is <= 1 or > 20)
            {
                return null;
            }

            var width = ToMillimeters(
                match.Groups["value"].Value,
                match.Groups["unit"].Value);
            return new GlassPaneGeometry(
                Enumerable.Repeat(new GlassPane(width, height), count).ToArray(),
                [GlassResolutionReasonCodes.GlassPaneDimensionsFromPocketLeaf],
                false,
                true);
        }

        private static int? TryExtractPocketLeafWidth(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\b(?:(?:ancho|width)\s+(?:de\s+)?(?:hoja|leaf|nave|panel\s+m[oó]vil|abertura|opening|vano\s+[uú]til|luz)|(?:hoja|leaf|nave|panel\s+m[oó]vil|abertura|opening|vano\s+[uú]til|luz)\s+(?:width|ancho))\b\s*(?:=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value)
                : null;
        }

        private static bool IsPocketDomain(RequirementExtractedItem item) =>
            AssociatedPocketContextTexts(item)
                .Any(text =>
                    Contains(text, "POCKET")
                    || Contains(text, "BOLSILLO"));

        private static IEnumerable<string?> AssociatedPocketContextTexts(
            RequirementExtractedItem item)
        {
            yield return item.Description;
            yield return item.FunctionalType;
            yield return item.Operation;
            yield return item.Arrangement;
            yield return item.Modulation;
            yield return item.OpeningDirection;
            yield return item.GeometryType;
            yield return item.AssemblyType;
            foreach (var feature in item.SpecialFeatures)
            {
                yield return feature;
            }

            foreach (var text in item.Evidence.Select(evidence => evidence.Text))
            {
                yield return text;
            }

            foreach (var segment in item.Segments)
            {
                yield return segment.Role;
                yield return segment.Operation;
                yield return segment.GeometryType;
                yield return segment.EvidenceText;
            }
        }

        private static bool IsRoofGlassDomain(RequirementExtractedItem item) =>
            item.ElementType == StructuredElementType.Skylight
            || Contains(item.FunctionalType, "SKYLIGHT")
            || Contains(item.FunctionalType, "CLARABOYA")
            || Contains(item.FunctionalType, "TECHO EN VIDRIO")
            || Contains(item.FunctionalType, "CUBIERTA EN VIDRIO")
            || Contains(item.FunctionalType, "ROOF GLASS")
            || Contains(item.FunctionalType, "GLASS ROOF")
            || Contains(item.Description, "CLARABOYA")
            || Contains(item.Description, "TECHO EN VIDRIO")
            || Contains(item.Description, "CUBIERTA EN VIDRIO")
            || Contains(item.Description, "ROOF GLASS")
            || Contains(item.Description, "GLASS ROOF");

        private static RoofGlassDimensions? TryExtractRoofLengthWidth(
            string text)
        {
            var length = TryExtractLabeledDimension(
                text,
                "(?:largo|length)");
            var width = TryExtractLabeledDimension(
                text,
                "(?:ancho|width)");
            return length is { } lengthMm && width is { } widthMm
                ? new RoofGlassDimensions(lengthMm, widthMm)
                : null;
        }

        private static int? TryExtractLabeledDimension(
            string text,
            string labelPattern)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                $@"\b{labelPattern}\b\s*(?:=|:|-)?\s*(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value)
                : null;
        }

        private static GlassPaneGeometry? TryResolveExplicitHorizontalPanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            var source = string.Join(
                " ",
                item.Modulation,
                item.Arrangement);
            if (!Contains(source, "HEIGHT")
                && !Contains(source, "ALTO")
                && !Contains(source, "HORIZONTAL"))
            {
                return null;
            }

            var heights = System.Text.RegularExpressions.Regex
                .Matches(source, @"\d{2,5}")
                .Select(match => int.Parse(
                    match.Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => value > 0)
                .ToArray();
            if (heights.Length < 2 || heights.Sum() != height)
            {
                return null;
            }

            return new GlassPaneGeometry(
                heights.Select(value => new GlassPane(width, value)).ToArray(),
                [GlassResolutionReasonCodes
                    .GlassPaneDimensionsFromSubmodules],
                false,
                true);
        }

        private static GlassPaneGeometry? TryResolveExplicitSegments(
            RequirementExtractedItem item)
        {
            var panes = item.Segments
                .Where(segment =>
                    segment.WidthMillimeters is > 0
                    && segment.HeightMillimeters is > 0)
                .Select(segment => new GlassPane(
                    segment.WidthMillimeters!.Value,
                    segment.HeightMillimeters!.Value))
                .ToArray();
            return panes.Length == 0
                ? null
                : new GlassPaneGeometry(
                    panes,
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromSubmodules],
                    false,
                    true);
        }

        private static GlassPaneGeometry? TryResolveExplicitPanePairs(
            RequirementExtractedItem item)
        {
            var modulationSource = string.Join(
                " ",
                item.Modulation,
                item.Arrangement);
            var modulationPanes = ExtractExplicitPanePairs(modulationSource);
            if (modulationPanes.Length > 0)
            {
                return new GlassPaneGeometry(
                    modulationPanes,
                    [GlassResolutionReasonCodes
                        .GlassPaneDimensionsFromSubmodules],
                    false,
                    true);
            }

            foreach (var text in AssociatedEvidenceTexts(item))
            {
                var panes = ExtractExplicitPanePairs(text);
                if (panes.Length == 0)
                {
                    continue;
                }

                return new GlassPaneGeometry(
                    panes,
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence],
                    false,
                    true);
            }

            return null;
        }

        private static GlassPaneGeometry? TryResolveExplicitVerticalPanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            var source = string.Join(
                " ",
                item.Modulation,
                item.Arrangement);
            if (!Contains(source, "WIDTH")
                && !Contains(source, "ANCHO")
                && !Contains(source, "VERTICAL"))
            {
                return null;
            }

            var widths = System.Text.RegularExpressions.Regex
                .Matches(source, @"\d{2,5}")
                .Select(match => int.Parse(
                    match.Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => value > 0)
                .ToArray();
            if (widths.Length < 2 || widths.Sum() != width)
            {
                return null;
            }

            return new GlassPaneGeometry(
                widths.Select(value => new GlassPane(value, height)).ToArray(),
                [GlassResolutionReasonCodes
                    .GlassPaneDimensionsFromSubmodules],
                false,
                true);
        }

        private static GlassPaneGeometry? TryResolveEvidencePanes(
            RequirementExtractedItem item,
            int width,
            int height)
        {
            foreach (var text in AssociatedEvidenceTexts(item))
            {
                var panes = TryResolveEvidencePanes(text, width, height);
                if (panes is not null)
                {
                    return panes;
                }
            }

            return null;
        }

        private static IEnumerable<string> AssociatedEvidenceTexts(
            RequirementExtractedItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Modulation))
            {
                yield return item.Modulation;
            }

            if (!string.IsNullOrWhiteSpace(item.Arrangement))
            {
                yield return item.Arrangement;
            }

            foreach (var evidence in item.Evidence)
            {
                if (!string.IsNullOrWhiteSpace(evidence.Text))
                {
                    yield return evidence.Text;
                }
            }

            foreach (var segment in item.Segments)
            {
                if (!string.IsNullOrWhiteSpace(segment.EvidenceText))
                {
                    yield return segment.EvidenceText;
                }
            }
        }

        private static GlassPaneGeometry? TryResolveEvidencePanes(
            string text,
            int width,
            int height)
        {
            var dimensions = ExtractExplicitDimensions(text);
            if (dimensions.Length == 0)
            {
                return null;
            }

            var expanded = ExpandRepeatedDimensions(text, dimensions);
            var orientation = ResolveSegmentationOrientation(text, expanded, width, height);
            return orientation switch
            {
                PaneSegmentationOrientation.Vertical => new GlassPaneGeometry(
                    expanded.Select(value => new GlassPane(width, value)).ToArray(),
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence],
                    false,
                    true),
                PaneSegmentationOrientation.Horizontal => new GlassPaneGeometry(
                    expanded.Select(value => new GlassPane(value, height)).ToArray(),
                    [GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence],
                    false,
                    true),
                _ => null
            };
        }

        private static int[] ExtractExplicitDimensions(string text) =>
            System.Text.RegularExpressions.Regex
                .Matches(
                    text,
                    @"(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mm|cm|m)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(match => ToMillimeters(
                    match.Groups["value"].Value,
                    match.Groups["unit"].Value))
                .Where(value => value > 0)
                .ToArray();

        private static int[] ExpandRepeatedDimensions(
            string text,
            int[] dimensions)
        {
            if (dimensions.Length != 1
                || !Contains(text, "CADA UNO"))
            {
                if (dimensions.Length < 2
                    || !Contains(text, "CADA UNO"))
                {
                    return dimensions;
                }
            }

            var count = System.Text.RegularExpressions.Regex
                .Match(
                    text,
                    @"(?<count>\d{1,2})\s*(tramos|panos|paños|cuerpos|segmentos)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!count.Success)
            {
                return dimensions;
            }

            var value = int.Parse(
                count.Groups["count"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return value is > 1 and <= 20
                ? Enumerable.Repeat(dimensions[^1], value).ToArray()
                : dimensions;
        }

        private static PaneSegmentationOrientation ResolveSegmentationOrientation(
            string text,
            int[] dimensions,
            int width,
            int height)
        {
            if (dimensions.Length < 2)
            {
                return PaneSegmentationOrientation.Unknown;
            }

            var verticalHint = Contains(text, "TRAMO SUPERIOR")
                || Contains(text, "SUPERIOR")
                || Contains(text, "CENTRAL")
                || Contains(text, "INFERIOR")
                || Contains(text, "ALTURA")
                || Contains(text, "ALTO")
                || Contains(text, "VERTICAL");
            var horizontalHint = Contains(text, "ANCHO")
                || Contains(text, "WIDTH")
                || Contains(text, "HORIZONTAL");

            var verticalMatches = SumMatches(dimensions, height);
            var horizontalMatches = SumMatches(dimensions, width);
            if (verticalHint && verticalMatches)
            {
                return PaneSegmentationOrientation.Vertical;
            }

            if (horizontalHint && horizontalMatches)
            {
                return PaneSegmentationOrientation.Horizontal;
            }

            if (verticalMatches && !horizontalMatches)
            {
                return PaneSegmentationOrientation.Vertical;
            }

            if (horizontalMatches && !verticalMatches)
            {
                return PaneSegmentationOrientation.Horizontal;
            }

            return PaneSegmentationOrientation.Unknown;
        }

        private static GlassPane[] ExtractExplicitPanePairs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            return System.Text.RegularExpressions.Regex
                .Matches(
                    text,
                    @"(?<width>\d{2,5}(?:[\.,]\d+)?)\s*(?<widthUnit>mm|cm|m)?\s*(?:x|×|por)\s*(?<height>\d{2,5}(?:[\.,]\d+)?)\s*(?<heightUnit>mm|cm|m)?\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(match =>
                {
                    var widthUnit = match.Groups["widthUnit"].Success
                        ? match.Groups["widthUnit"].Value
                        : "mm";
                    var heightUnit = match.Groups["heightUnit"].Success
                        ? match.Groups["heightUnit"].Value
                        : widthUnit;
                    return new GlassPane(
                        ToMillimeters(match.Groups["width"].Value, widthUnit),
                        ToMillimeters(match.Groups["height"].Value, heightUnit));
                })
                .Where(pane => pane.WidthMm >= 100
                    && pane.HeightMm >= 100
                    && pane.WidthMm <= 10000
                    && pane.HeightMm <= 10000)
                .ToArray();
        }

        private static bool SumMatches(int[] values, int expected) =>
            Math.Abs(values.Sum() - expected)
            <= GlassPaneSegmentationToleranceMillimeters;

        private static int ToMillimeters(string value, string unit)
        {
            var number = decimal.Parse(
                value.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture);
            var multiplier = unit.Trim().ToUpperInvariant() switch
            {
                "M" => 1000m,
                "CM" => 10m,
                _ => 1m
            };

            return (int)Math.Round(number * multiplier, MidpointRounding.AwayFromZero);
        }

        private enum PaneSegmentationOrientation
        {
            Unknown,
            Vertical,
            Horizontal
        }

        private sealed record RoofGlassDimensions(int LengthMm, int WidthMm);
    }

    private static bool IsShowerDivision(RequirementExtractedItem item) =>
        item.ElementType == StructuredElementType.ShowerDivision
        || Contains(item.FunctionalType, "SHOWER_DIVISION")
        || Contains(item.FunctionalType, "BATHROOM_DIVISION")
        || Contains(item.Description, "DIVISION DE BANO")
        || Contains(item.Description, "DIVISION DE BAÑO");

    private static bool IsRailingOrGuardrail(RequirementExtractedItem item) =>
        item.ElementType == StructuredElementType.Railing
        || Contains(item.FunctionalType, "RAILING")
        || Contains(item.FunctionalType, "BARANDA")
        || Contains(item.Description, "BARANDA")
        || Contains(item.Description, "ESCALERA")
        || Contains(item.Description, "PASAMANOS");

    private static bool IsExplicitSpecialGlassVariant(
        RequirementExtractedItem item) =>
        Contains(item.GlassRawSpecification, "GRIS")
        || Contains(item.GlassRawSpecification, "GRAY")
        || Contains(item.GlassRawSpecification, "QUALITY GLASS")
        || Contains(item.GlassRawSpecification, "CL120")
        || Contains(item.GlassRawSpecification, "CL150")
        || Contains(item.GlassRawSpecification, "CL167")
        || Contains(item.GlassTypeRaw, "GRIS")
        || Contains(item.GlassTypeRaw, "GRAY")
        || Contains(item.GlassTypeRaw, "QUALITY GLASS")
        || Contains(item.GlassTypeNormalized, "GRIS")
        || Contains(item.GlassTypeNormalized, "GRAY")
        || Contains(item.GlassTypeNormalized, "QUALITY GLASS");

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
}
