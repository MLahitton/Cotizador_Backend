using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.Common.Abstractions.Catalogs;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IGlassCandidateResolver
{
    GlassCandidateResolutionResult Resolve(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog);

    GlassCandidateResolutionResult Resolve(
        GlassCandidateResolutionInput input,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog);

    IReadOnlyList<GlassCandidateResolutionResult> ResolveMany(
        IReadOnlyList<GlassCandidateResolutionInput> inputs,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog);
}

public sealed record GlassCandidateResolutionInput(
    string? GlassRawSpecification,
    string? GlassTypeRaw,
    string? GlassTypeNormalized,
    decimal? GlassThicknessMm,
    string? GlassColorRaw,
    string? GlassColorNormalized,
    string? GlassTreatmentRaw,
    string? GlassTreatmentNormalized,
    string? GlassComposition,
    string? GlassCoating,
    string? GlassTransparency);

public sealed record GlassCandidateResolutionResult(
    GlassCandidateAlternative? Suggested,
    IReadOnlyList<GlassCandidateAlternative> Alternatives,
    decimal Confidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> ResolutionReasons);

public sealed record GlassCandidateAlternative(
    Guid GlassTypeId,
    string Code,
    string DisplayName,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public static class GlassResolutionReviewReasons
{
    public const string GlassNotSpecified = "GLASS_NOT_SPECIFIED";
    public const string GlassThicknessMissing = "GLASS_THICKNESS_MISSING";
    public const string GlassNoCompatibleCandidate =
        "GLASS_NO_COMPATIBLE_CANDIDATE";
    public const string GlassAmbiguous = "GLASS_AMBIGUOUS";
    public const string GlassConflictingSignals = "GLASS_CONFLICTING_SIGNALS";
}

public static class GlassResolutionReasonCodes
{
    public const string FamilyMatched = "FAMILY_MATCHED";
    public const string CompositionMatched = "COMPOSITION_MATCHED";
    public const string ThicknessMatched = "THICKNESS_MATCHED";
    public const string PvbMatched = "PVB_MATCHED";
    public const string ChamberMatched = "CHAMBER_MATCHED";
    public const string ColorMatched = "COLOR_MATCHED";
    public const string PatternMatched = "PATTERN_MATCHED";
    public const string ProductCodeMatched = "PRODUCT_CODE_MATCHED";
    public const string SecondaryAttributesMissing =
        "SECONDARY_ATTRIBUTES_MISSING";
    public const string GlassLineTempered = "GLASS_LINE_TEMPERED";
    public const string GlassLineLaminated = "GLASS_LINE_LAMINATED";
    public const string JointGlassRule = "JOINT_GLASS_RULE";
    public const string NarrowGlassHeightExtension =
        "NARROW_GLASS_HEIGHT_EXTENSION";
    public const string GlassPaneDimensionsFromElement =
        "GLASS_PANE_DIMENSIONS_FROM_ELEMENT";
    public const string GlassPaneDimensionsFromModulation =
        "GLASS_PANE_DIMENSIONS_FROM_MODULATION";
    public const string GlassPaneDimensionsFromSubmodules =
        "GLASS_PANE_DIMENSIONS_FROM_SUBMODULES";
    public const string GlassPaneGeometryUnresolved =
        "GLASS_PANE_GEOMETRY_UNRESOLVED";
    public const string GlassPaneHeterogeneousNeeds =
        "GLASS_PANE_HETEROGENEOUS_NEEDS";
    public const string SpecialGlassShower8Mm = "SPECIAL_GLASS_SHOWER_8MM";
    public const string SpecialGlassRailing10Mm = "SPECIAL_GLASS_RAILING_10MM";
    public const string Laminated55JointAndHeight =
        "LAMINATED_5_5_JOINT_AND_HEIGHT";
}

public sealed class GlassCandidateResolver : IGlassCandidateResolver
{
    private const decimal ExactConfidence = 1.00m;
    private const decimal NormalizedConfidence = 0.85m;
    private const decimal PartialConfidence = 0.60m;

    public GlassCandidateResolutionResult Resolve(
        RequirementExtractedItem item,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Resolve(
            new GlassCandidateResolutionInput(
                item.GlassRawSpecification,
                item.GlassTypeRaw,
                item.GlassTypeNormalized,
                item.GlassThicknessMm,
                item.GlassColorRaw,
                item.GlassColorNormalized,
                item.GlassTreatmentRaw,
                item.GlassTreatmentNormalized,
                item.GlassComposition,
                item.GlassCoating,
                item.GlassTransparency),
            catalog);
    }

    public IReadOnlyList<GlassCandidateResolutionResult> ResolveMany(
        IReadOnlyList<GlassCandidateResolutionInput> inputs,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(catalog);

        return inputs.Select(input => Resolve(input, catalog)).ToArray();
    }

    public GlassCandidateResolutionResult Resolve(
        GlassCandidateResolutionInput input,
        IReadOnlyList<GlassTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);

        var signal = GlassSignal.From(input);
        var candidates = catalog
            .Where(value => value.IsActive)
            .Where(value => !value.Code.Equals(
                "UNKNOWN_GLASS",
                StringComparison.OrdinalIgnoreCase))
            .Select(CatalogGlass.From)
            .ToArray();

        if (!signal.HasAnyGlassSignal)
        {
            return Review(
                GlassResolutionReviewReasons.GlassNotSpecified,
                candidates.Select(value => value.ToAlternative(
                    PartialConfidence,
                    [GlassResolutionReasonCodes.SecondaryAttributesMissing]))
                    .Take(10)
                    .ToArray());
        }

        if (signal.HasFamilyConflict)
        {
            return Review(GlassResolutionReviewReasons.GlassConflictingSignals);
        }

        var productMatches = ProductMatches(signal, candidates);
        if (productMatches.Length == 1)
        {
            return Unique(productMatches[0], ExactConfidence,
                [GlassResolutionReasonCodes.ProductCodeMatched]);
        }

        if (productMatches.Length > 1)
        {
            return Ambiguous(productMatches);
        }

        var familyMatches = signal.Family is null
            ? []
            : candidates
                .Where(candidate => candidate.Family == signal.Family)
                .ToArray();

        if (signal.Family is null || familyMatches.Length == 0)
        {
            return Review(GlassResolutionReviewReasons
                .GlassNoCompatibleCandidate);
        }

        var compatible = familyMatches;
        var reasons = new List<string> { GlassResolutionReasonCodes.FamilyMatched };

        if (signal.Composition is { } composition)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesComposition(composition))
                .ToArray();
            reasons.Add(GlassResolutionReasonCodes.CompositionMatched);
        }

        if (signal.ThicknessMm is { } thickness
            && signal.LeafThicknesses.Count <= 1)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesThickness(thickness))
                .ToArray();
            reasons.Add(GlassResolutionReasonCodes.ThicknessMatched);
        }
        else if (signal.Family is GlassFamily.Monolithic
            && signal.Composition is GlassComposition.Tempered)
        {
            return Review(
                GlassResolutionReviewReasons.GlassThicknessMissing,
                compatible.Select(value => value.ToAlternative(
                    PartialConfidence,
                    [GlassResolutionReasonCodes.FamilyMatched]))
                    .ToArray());
        }

        if (compatible.Length == 0)
        {
            return Review(GlassResolutionReviewReasons
                .GlassNoCompatibleCandidate);
        }

        if (signal.LeafThicknesses.Count > 1)
        {
            var leafMatches = compatible
                .Where(candidate => candidate.MatchesLeafThicknesses(
                    signal.LeafThicknesses))
                .ToArray();
            if (leafMatches.Length > 0)
            {
                compatible = leafMatches;
                if (!reasons.Contains(GlassResolutionReasonCodes
                        .ThicknessMatched, StringComparer.Ordinal))
                {
                    reasons.Add(GlassResolutionReasonCodes.ThicknessMatched);
                }
            }
        }

        if (signal.PvbThicknessMm is { } pvbThickness)
        {
            var pvbMatches = compatible
                .Where(candidate => candidate.MatchesPvbThickness(pvbThickness))
                .ToArray();
            if (pvbMatches.Length > 0)
            {
                compatible = pvbMatches;
                reasons.Add(GlassResolutionReasonCodes.PvbMatched);
            }
        }

        if (signal.ChamberThicknessMm is { } chamberThickness)
        {
            var chamberMatches = compatible
                .Where(candidate => candidate.MatchesChamberThickness(
                    chamberThickness))
                .ToArray();
            if (chamberMatches.Length > 0)
            {
                compatible = chamberMatches;
                reasons.Add(GlassResolutionReasonCodes.ChamberMatched);
            }
        }

        if (signal.Color is { } color)
        {
            var colorMatches = compatible
                .Where(candidate => candidate.MatchesColor(color))
                .ToArray();
            if (colorMatches.Length > 0)
            {
                compatible = colorMatches;
                reasons.Add(GlassResolutionReasonCodes.ColorMatched);
            }
        }

        if (signal.Pattern is { } pattern)
        {
            var patternMatches = compatible
                .Where(candidate => candidate.MatchesPattern(pattern))
                .ToArray();
            if (patternMatches.Length > 0)
            {
                compatible = patternMatches;
                reasons.Add(GlassResolutionReasonCodes.PatternMatched);
            }
        }

        if (signal.ProductToken is { } productToken)
        {
            var compatibleProductMatches = compatible
                .Where(candidate => candidate.MatchesProductToken(productToken))
                .ToArray();
            if (compatibleProductMatches.Length > 0)
            {
                compatible = compatibleProductMatches;
                reasons.Add(GlassResolutionReasonCodes.ProductCodeMatched);
            }
        }

        if (compatible.Length == 1)
        {
            var confidence = reasons.Count >= 2
                ? ExactConfidence
                : NormalizedConfidence;
            return Unique(compatible[0], confidence, reasons);
        }

        return Ambiguous(compatible);
    }

    private static CatalogGlass[] ProductMatches(
        GlassSignal signal,
        IReadOnlyList<CatalogGlass> candidates) =>
        signal.ProductToken is null
            ? []
            : candidates
                .Where(candidate => candidate.MatchesProductToken(
                    signal.ProductToken))
                .ToArray();

    private static GlassCandidateResolutionResult Unique(
        CatalogGlass candidate,
        decimal confidence,
        IReadOnlyList<string> reasons) =>
        new(
            candidate.ToAlternative(confidence, reasons),
            [candidate.ToAlternative(confidence, reasons)],
            confidence,
            false,
            [],
            reasons);

    private static GlassCandidateResolutionResult Ambiguous(
        IReadOnlyList<CatalogGlass> candidates)
    {
        var alternatives = candidates
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .Select(value => value.ToAlternative(
                PartialConfidence,
                [GlassResolutionReasonCodes.FamilyMatched]))
            .ToArray();
        return new(
            null,
            alternatives,
            0m,
            true,
                [GlassResolutionReviewReasons.GlassAmbiguous],
            []);
    }

    private static GlassCandidateResolutionResult Review(
        string reason,
        IReadOnlyList<GlassCandidateAlternative>? alternatives = null) =>
        new(null, alternatives ?? [], 0m, true, [reason], []);

    private sealed record GlassSignal(
        GlassFamily? Family,
        decimal? ThicknessMm,
        IReadOnlyList<decimal> LeafThicknesses,
        GlassComposition? Composition,
        GlassColor? Color,
        decimal? PvbThicknessMm,
        decimal? ChamberThicknessMm,
        string? Pattern,
        string? ProductToken,
        bool HasAnyGlassSignal,
        bool HasFamilyConflict)
    {
        public static GlassSignal From(GlassCandidateResolutionInput input)
        {
            var joined = Join(
                input.GlassRawSpecification,
                input.GlassTypeRaw,
                input.GlassTypeNormalized,
                input.GlassComposition,
                input.GlassColorRaw,
                input.GlassColorNormalized,
                input.GlassTreatmentRaw,
                input.GlassTreatmentNormalized,
                input.GlassCoating,
                input.GlassTransparency);
            var normalized = Normalize(joined);
            var family = FamilyFrom(normalized);
            var rawFamily = FamilyFrom(Normalize(input.GlassTypeRaw));
            var compositionFamily = FamilyFrom(Normalize(input.GlassComposition));
            var hasConflict = rawFamily is not null
                && compositionFamily is not null
                && rawFamily != compositionFamily;
            var thickness = input.GlassThicknessMm ?? ThicknessFrom(normalized);
            var leafThicknesses = LeafThicknessesFrom(normalized);
            var composition = CompositionFrom(normalized);
            var color = ColorFrom(normalized);
            var pvbThickness = PvbThicknessFrom(normalized);
            var chamberThickness = ChamberThicknessFrom(normalized);
            var pattern = PatternFrom(normalized);
            var productToken = ProductTokenFrom(normalized);

            return new(
                family,
                thickness,
                leafThicknesses,
                composition,
                color,
                pvbThickness,
                chamberThickness,
                pattern,
                productToken,
                !string.IsNullOrWhiteSpace(joined) || input.GlassThicknessMm is not null,
                hasConflict);
        }

        private static GlassFamily? FamilyFrom(string? normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (normalized.Contains("N.A.", StringComparison.Ordinal)
                || normalized.Equals("NA", StringComparison.Ordinal))
            {
                return GlassFamily.NotApplicable;
            }

            if (normalized.Contains("CAMARA", StringComparison.Ordinal)
                || normalized.Contains("CÁMARA", StringComparison.Ordinal))
            {
                return GlassFamily.Igu;
            }

            if (normalized.Contains("MONOLITICO", StringComparison.Ordinal)
                || normalized.Contains("MONOLITHIC", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            if (normalized.Contains("LAMINADO", StringComparison.Ordinal)
                || normalized.Contains("LAMINATED", StringComparison.Ordinal)
                || normalized.Contains("PVB", StringComparison.Ordinal)
                || normalized.StartsWith("LAM_", StringComparison.Ordinal))
            {
                return GlassFamily.Laminated;
            }

            if (normalized.Contains("TEMPLADO", StringComparison.Ordinal)
                || normalized.Contains("TEMPERED", StringComparison.Ordinal)
                || normalized.StartsWith("TEMP_", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            if (normalized.Contains("CRUDO", StringComparison.Ordinal)
                || normalized.Contains("RAW", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            return null;
        }

        private static GlassComposition? CompositionFrom(string normalized)
        {
            if (normalized.Contains("TEMPLADO", StringComparison.Ordinal)
                || normalized.Contains("TEMPERED", StringComparison.Ordinal)
                || normalized.StartsWith("TEMP_", StringComparison.Ordinal))
            {
                return GlassComposition.Tempered;
            }

            if (normalized.Contains("CRUDO", StringComparison.Ordinal)
                || normalized.Contains("RAW", StringComparison.Ordinal))
            {
                return GlassComposition.Raw;
            }

            return null;
        }

        private static GlassColor? ColorFrom(string normalized)
        {
            if (normalized.Contains("GRIS", StringComparison.Ordinal)
                || normalized.Contains("GRAY", StringComparison.Ordinal))
            {
                return GlassColor.Gray;
            }

            if (normalized.Contains("INCOLORO", StringComparison.Ordinal)
                || normalized.Contains("CLEAR", StringComparison.Ordinal)
                || normalized.Contains(" INC", StringComparison.Ordinal))
            {
                return GlassColor.Clear;
            }

            if (normalized.Contains("QUALITY GLASS", StringComparison.Ordinal))
            {
                if (normalized.Contains("GREEN", StringComparison.Ordinal))
                {
                    return GlassColor.Green;
                }

                if (normalized.Contains("BLUE", StringComparison.Ordinal))
                {
                    return GlassColor.Blue;
                }

                if (normalized.Contains("BRONZE", StringComparison.Ordinal))
                {
                    return GlassColor.Bronze;
                }
            }

            return null;
        }

        private static decimal? ThicknessFrom(string normalized)
        {
            var match = Regex.Match(
                normalized,
                @"(?<!\d)(4|5|6|8|10)(?:[.,]0+)?\s*MM\b",
                RegexOptions.CultureInvariant);
            return match.Success
                ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
        }

        private static IReadOnlyList<decimal> LeafThicknessesFrom(
            string normalized)
        {
            var matches = Regex.Matches(
                normalized,
                @"(?<!PVB\s)(?<!CAMARA\s)(?<!CÁMARA\s)(?<!\d)(4|5|6|8|10)(?:[.,]0+)?\s*MM\b",
                RegexOptions.CultureInvariant);
            return matches
                .Select(match => decimal.Parse(
                    match.Groups[1].Value,
                    CultureInfo.InvariantCulture))
                .Distinct()
                .Order()
                .ToArray();
        }

        private static decimal? PvbThicknessFrom(string normalized)
        {
            var match = Regex.Match(
                normalized,
                @"PVB\s+(0[.,]38|0[.,]76|1[.,]14|1[.,]52)(?:\s*MM)?",
                RegexOptions.CultureInvariant);
            return match.Success
                ? decimal.Parse(
                    match.Groups[1].Value.Replace(',', '.'),
                    CultureInfo.InvariantCulture)
                : null;
        }

        private static decimal? ChamberThicknessFrom(string normalized)
        {
            var match = Regex.Match(
                normalized,
                @"C[ÁA]MARA\s+(12)(?:\s*MM)?",
                RegexOptions.CultureInvariant);
            return match.Success
                ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
        }

        private static string? PatternFrom(string normalized) =>
            normalized.Contains("MINI BOREAL", StringComparison.Ordinal)
                ? "MINI_BOREAL"
                : null;

        private static string? ProductTokenFrom(string normalized)
        {
            var match = Regex.Match(
                normalized.Contains("QUALITY GLASS", StringComparison.Ordinal)
                    ? normalized
                    : normalized.Replace("GREEN", string.Empty, StringComparison.Ordinal)
                        .Replace("BLUE", string.Empty, StringComparison.Ordinal)
                        .Replace("BRONZE", string.Empty, StringComparison.Ordinal),
                @"\b(CL167|CL120|CL150|GREEN|BLUE|BRONZE)\b",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    private sealed record CatalogGlass(
        Guid Id,
        string Code,
        string Name,
        GlassFamily? Family,
        GlassComposition? Composition,
        decimal? OuterThicknessMm,
        decimal? InnerThicknessMm,
        GlassColor? Color,
        decimal? PvbThicknessMm,
        decimal? ChamberThicknessMm,
        string? Pattern,
        string? ProductToken)
    {
        public static CatalogGlass From(GlassTypeCatalogReadModel model)
        {
            var normalized = Normalize(Join(model.Code, model.Name));
            return new(
                model.GlassTypeId,
                model.Code,
                model.Name,
                FamilyFromMetadata(model.Family)
                    ?? FamilyFromCodeOrName(normalized),
                CompositionFromMetadata(model.Composition)
                    ?? CompositionFromCodeOrName(normalized),
                model.OuterThicknessMm
                    ?? ThicknessFromCodeOrName(model.Code, normalized),
                model.InnerThicknessMm,
                ColorFromMetadata(model.Color ?? model.PvbColor)
                    ?? ColorFromCodeOrName(model.Code, normalized),
                model.PvbThicknessMm,
                model.ChamberThicknessMm,
                model.Pattern,
                ProductTokenFromCatalog(normalized));
        }

        public bool MatchesComposition(GlassComposition composition) =>
            Composition is null || Composition == composition;

        public bool MatchesThickness(decimal thickness) =>
            OuterThicknessMm is { } current
            && current == thickness
            && (Family != GlassFamily.Laminated
                || InnerThicknessMm is null
                || InnerThicknessMm == thickness);

        public bool MatchesLeafThicknesses(IReadOnlyList<decimal> values)
        {
            if (OuterThicknessMm is null)
            {
                return false;
            }

            decimal[] current = InnerThicknessMm is null
                ? [OuterThicknessMm.Value]
                : [OuterThicknessMm.Value, InnerThicknessMm.Value];
            return current.OrderBy(value => value)
                .SequenceEqual(values.OrderBy(value => value));
        }

        public bool MatchesPvbThickness(decimal value) =>
            PvbThicknessMm is { } current && current == value;

        public bool MatchesChamberThickness(decimal value) =>
            ChamberThicknessMm is { } current && current == value;

        public bool MatchesPattern(string value) =>
            Pattern is not null
            && Pattern.Equals(value, StringComparison.Ordinal);

        public bool MatchesColor(GlassColor color) =>
            Color is null
            || Color == color
            || color == GlassColor.Clear && Color == GlassColor.Clear;

        public bool MatchesProductToken(string token) =>
            ProductToken is not null
            && ProductToken.Equals(token, StringComparison.Ordinal);

        public GlassCandidateAlternative ToAlternative(
            decimal confidence,
            IReadOnlyList<string> reasons) =>
            new(Id, Code, Name, confidence, reasons);

        private static decimal? ThicknessFromCodeOrName(
            string code,
            string normalized)
        {
            var codeMatch = Regex.Match(
                code,
                @"(?:TEMP_|LAM_)(\d+)(?:_|$)",
                RegexOptions.CultureInvariant);
            if (codeMatch.Success)
            {
                var value = codeMatch.Groups[1].Value;
                return value.Length == 2 && value[0] == value[1]
                    ? decimal.Parse(value[0].ToString(), CultureInfo.InvariantCulture)
                    : decimal.Parse(value, CultureInfo.InvariantCulture);
            }

            var textMatch = Regex.Match(
                normalized,
                @"(?<!\d)(4|5|6|8|10)(?:[.,]0+)?\s*MM\b",
                RegexOptions.CultureInvariant);
            return textMatch.Success
                ? decimal.Parse(textMatch.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
        }

        private static GlassFamily? FamilyFromCodeOrName(string normalized)
        {
            if (normalized.Contains("N.A.", StringComparison.Ordinal))
            {
                return GlassFamily.NotApplicable;
            }

            if (normalized.Contains("CAMARA", StringComparison.Ordinal)
                || normalized.Contains("CÁMARA", StringComparison.Ordinal))
            {
                return GlassFamily.Igu;
            }

            if (normalized.Contains("MONOLITICO", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            if (normalized.Contains("LAMINADO", StringComparison.Ordinal)
                || normalized.Contains("LAMINATED", StringComparison.Ordinal)
                || normalized.Contains("PVB", StringComparison.Ordinal)
                || normalized.Contains("LAM_", StringComparison.Ordinal))
            {
                return GlassFamily.Laminated;
            }

            if (normalized.Contains("TEMPLADO", StringComparison.Ordinal)
                || normalized.Contains("TEMPERED", StringComparison.Ordinal)
                || normalized.Contains("TEMP_", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            if (normalized.Contains("CRUDO", StringComparison.Ordinal)
                || normalized.Contains("RAW", StringComparison.Ordinal))
            {
                return GlassFamily.Monolithic;
            }

            return null;
        }

        private static GlassFamily? FamilyFromMetadata(string? value) =>
            Normalize(value) switch
            {
                "MONOLITHIC" => GlassFamily.Monolithic,
                "MONOLITICO" => GlassFamily.Monolithic,
                "LAMINATED" => GlassFamily.Laminated,
                "LAMINADO" => GlassFamily.Laminated,
                "IGU" => GlassFamily.Igu,
                "NOT_APPLICABLE" => GlassFamily.NotApplicable,
                _ => null
            };

        private static GlassComposition? CompositionFromMetadata(
            string? value) =>
            Normalize(value) switch
            {
                "TEMPERED" => GlassComposition.Tempered,
                "TEMPLADO" => GlassComposition.Tempered,
                "RAW" => GlassComposition.Raw,
                "CRUDO" => GlassComposition.Raw,
                _ => null
            };

        private static GlassComposition? CompositionFromCodeOrName(
            string normalized)
        {
            if (normalized.Contains("TEMPLADO", StringComparison.Ordinal)
                || normalized.Contains("TEMPERED", StringComparison.Ordinal)
                || normalized.Contains("TEMP_", StringComparison.Ordinal))
            {
                return GlassComposition.Tempered;
            }

            if (normalized.Contains("CRUDO", StringComparison.Ordinal)
                || normalized.Contains("RAW", StringComparison.Ordinal))
            {
                return GlassComposition.Raw;
            }

            return null;
        }

        private static GlassColor? ColorFromMetadata(string? value) =>
            Normalize(value) switch
            {
                "INC" => GlassColor.Clear,
                "INCOLORO" => GlassColor.Clear,
                "CLEAR" => GlassColor.Clear,
                "GRIS" => GlassColor.Gray,
                "GRAY" => GlassColor.Gray,
                "GREEN" => GlassColor.Green,
                "BLUE" => GlassColor.Blue,
                "BRONZE" => GlassColor.Bronze,
                _ => null
            };

        private static GlassColor? ColorFromCodeOrName(
            string code,
            string normalized)
        {
            if (code.Contains("GRAY", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("GRIS", StringComparison.Ordinal)
                || normalized.Contains("GRAY", StringComparison.Ordinal))
            {
                return GlassColor.Gray;
            }

            if (normalized.Contains("INC", StringComparison.Ordinal)
                || normalized.Contains("INCOLORO", StringComparison.Ordinal)
                || normalized.Contains("CLEAR", StringComparison.Ordinal))
            {
                return GlassColor.Clear;
            }

            return null;
        }

        private static string? ProductTokenFromCatalog(string normalized)
        {
            var match = Regex.Match(
                normalized,
                @"\b(CL167|CL120|CL150|GREEN|BLUE|BRONZE)\b",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    private enum GlassFamily { Monolithic, Laminated, Igu, NotApplicable }

    private enum GlassComposition { Tempered, Raw }

    private enum GlassColor { Clear, Gray, Green, Blue, Bronze }

    private static string Join(params string?[] values) =>
        string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        var previousWasWhiteSpace = false;

        foreach (var character in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalized = character == ',' ? '.' : character;
            if (char.IsWhiteSpace(normalized))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }
                continue;
            }

            builder.Append(normalized);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
