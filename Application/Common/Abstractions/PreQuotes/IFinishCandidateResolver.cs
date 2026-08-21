using System.Globalization;
using System.Text;
using Application.Common.Abstractions.Catalogs;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IFinishCandidateResolver
{
    FinishCandidateResolutionResult Resolve(
        RequirementExtractedItem item,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog);

    FinishCandidateResolutionResult Resolve(
        FinishCandidateResolutionInput input,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog);

    IReadOnlyList<FinishCandidateResolutionResult> ResolveMany(
        IReadOnlyList<FinishCandidateResolutionInput> inputs,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog);
}

public sealed record FinishCandidateResolutionInput(
    string? FinishRawDescription,
    string? FinishNormalizedType,
    string? FinishColorRaw,
    string? FinishColorNormalized,
    string? FinishTextureRaw,
    string? FinishTextureNormalized,
    string? FinishExplicitCode,
    bool? FinishRequiresReview);

public sealed record FinishCandidateResolutionResult(
    FinishCandidateAlternative? Suggested,
    IReadOnlyList<FinishCandidateAlternative> Alternatives,
    decimal Confidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> ResolutionReasons);

public sealed record FinishCandidateAlternative(
    Guid FinishTypeId,
    string Code,
    string DisplayName,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public static class FinishResolutionReviewReasons
{
    public const string FinishNotSpecified = "FINISH_NOT_SPECIFIED";
    public const string FinishNoCompatibleCandidate =
        "FINISH_NO_COMPATIBLE_CANDIDATE";
    public const string FinishAmbiguous = "FINISH_AMBIGUOUS";
    public const string FinishConflictingSignals =
        "FINISH_CONFLICTING_SIGNALS";
}

public static class FinishResolutionReasonCodes
{
    public const string CommercialCodeMatched = "COMMERCIAL_CODE_MATCHED";
    public const string NormalizedTypeMatched = "NORMALIZED_TYPE_MATCHED";
    public const string MaterialMatched = "MATERIAL_MATCHED";
    public const string ColorMatched = "COLOR_MATCHED";
    public const string TextureMatched = "TEXTURE_MATCHED";
    public const string ProcessMatched = "PROCESS_MATCHED";
    public const string RawTextMatched = "RAW_TEXT_MATCHED";
    public const string SecondaryAttributesMissing =
        "SECONDARY_ATTRIBUTES_MISSING";
}

public sealed class FinishCandidateResolver : IFinishCandidateResolver
{
    private const decimal ExplicitCodeConfidence = 1.00m;
    private const decimal CompleteMetadataConfidence = 0.95m;
    private const decimal UniqueRawConfidence = 0.85m;
    private const decimal ProbableConfidence = 0.60m;

    public FinishCandidateResolutionResult Resolve(
        RequirementExtractedItem item,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Resolve(
            new FinishCandidateResolutionInput(
                item.FinishRawDescription,
                item.FinishNormalizedType,
                item.FinishColorRaw,
                item.FinishColorNormalized,
                item.FinishTextureRaw,
                item.FinishTextureNormalized,
                item.FinishExplicitCode,
                item.FinishRequiresReview),
            catalog);
    }

    public IReadOnlyList<FinishCandidateResolutionResult> ResolveMany(
        IReadOnlyList<FinishCandidateResolutionInput> inputs,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(catalog);

        return inputs.Select(input => Resolve(input, catalog)).ToArray();
    }

    public FinishCandidateResolutionResult Resolve(
        FinishCandidateResolutionInput input,
        IReadOnlyList<FinishTypeCatalogReadModel> catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);

        var signal = FinishSignal.From(input);
        var candidates = catalog
            .Where(value => value.IsActive && value.IsSelectable)
            .Select(CatalogFinish.From)
            .ToArray();

        if (!signal.HasAnySignal)
        {
            return Review(
                FinishResolutionReviewReasons.FinishNotSpecified,
                candidates.Select(value => value.ToAlternative(
                    ProbableConfidence,
                    [FinishResolutionReasonCodes.SecondaryAttributesMissing]))
                    .Take(10)
                    .ToArray());
        }

        if (signal.HasUnrecognizedExplicitColor)
        {
            return Review(FinishResolutionReviewReasons
                .FinishNoCompatibleCandidate);
        }

        if (!signal.HasRecognizedSignal)
        {
            var rawMatches = RawMatches(signal, candidates);
            return rawMatches.Length == 1
                ? Unique(
                    rawMatches[0],
                    UniqueRawConfidence,
                    [FinishResolutionReasonCodes.RawTextMatched])
                : rawMatches.Length > 1
                    ? Ambiguous(rawMatches)
                    : Review(FinishResolutionReviewReasons
                        .FinishNoCompatibleCandidate);
        }

        if (signal.CommercialCode is { } commercialCode)
        {
            var explicitMatches = candidates
                .Where(candidate => candidate.MatchesCommercialCode(
                    commercialCode))
                .ToArray();
            if (explicitMatches.Length == 0)
            {
                return Review(FinishResolutionReviewReasons
                    .FinishNoCompatibleCandidate);
            }

            var compatibleExplicit = FilterByMetadata(
                explicitMatches,
                signal,
                out var explicitReasons);
            if (compatibleExplicit.Length == 1)
            {
                return Unique(
                    compatibleExplicit[0],
                    ExplicitCodeConfidence,
                    [
                        FinishResolutionReasonCodes.CommercialCodeMatched,
                        .. explicitReasons
                    ]);
            }

            return compatibleExplicit.Length == 0
                ? Review(FinishResolutionReviewReasons
                    .FinishConflictingSignals)
                : Ambiguous(compatibleExplicit);
        }

        var compatible = FilterByMetadata(
            candidates,
            signal,
            out var reasons);

        if (reasons.Count == 0)
        {
            var rawMatches = RawMatches(signal, candidates);
            return rawMatches.Length == 1
                ? Unique(
                    rawMatches[0],
                    UniqueRawConfidence,
                    [FinishResolutionReasonCodes.RawTextMatched])
                : rawMatches.Length > 1
                    ? Ambiguous(rawMatches)
                    : Review(FinishResolutionReviewReasons
                        .FinishNoCompatibleCandidate);
        }

        if (compatible.Length == 0)
        {
            var rawMatches = RawMatches(signal, candidates);
            return rawMatches.Length == 1
                ? Unique(
                    rawMatches[0],
                    UniqueRawConfidence,
                    [FinishResolutionReasonCodes.RawTextMatched])
                : rawMatches.Length > 1
                    ? Ambiguous(rawMatches)
                    : Review(FinishResolutionReviewReasons
                        .FinishNoCompatibleCandidate);
        }

        if (compatible.Length == 1)
        {
            var confidence = reasons.Contains(
                FinishResolutionReasonCodes.NormalizedTypeMatched,
                StringComparer.Ordinal)
                && reasons.Contains(
                    FinishResolutionReasonCodes.ColorMatched,
                    StringComparer.Ordinal)
                ? CompleteMetadataConfidence
                : UniqueRawConfidence;
            return Unique(compatible[0], confidence, reasons);
        }

        var compatibleRawMatches = RawMatches(signal, compatible);
        if (compatibleRawMatches.Length == 1)
        {
            return Unique(
                compatibleRawMatches[0],
                UniqueRawConfidence,
                [.. reasons, FinishResolutionReasonCodes.RawTextMatched]);
        }

        return Ambiguous(compatibleRawMatches.Length > 1
            ? compatibleRawMatches
            : compatible);
    }

    private static CatalogFinish[] FilterByMetadata(
        IReadOnlyList<CatalogFinish> candidates,
        FinishSignal signal,
        out IReadOnlyList<string> reasons)
    {
        var compatible = candidates;
        var matched = new List<string>();

        if (signal.NormalizedType is { } type)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesNormalizedType(type))
                .ToArray();
            matched.Add(FinishResolutionReasonCodes.NormalizedTypeMatched);
        }

        if (signal.Material is { } material)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesMaterial(material))
                .ToArray();
            matched.Add(FinishResolutionReasonCodes.MaterialMatched);
        }

        if (signal.Color is { } color)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesColor(color))
                .ToArray();
            matched.Add(FinishResolutionReasonCodes.ColorMatched);
        }

        if (signal.Texture is { } texture)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesTexture(texture))
                .ToArray();
            matched.Add(FinishResolutionReasonCodes.TextureMatched);
        }

        if (signal.Process is { } process)
        {
            compatible = compatible
                .Where(candidate => candidate.MatchesProcess(process))
                .ToArray();
            matched.Add(FinishResolutionReasonCodes.ProcessMatched);
        }

        reasons = matched;
        return compatible.ToArray();
    }

    private static CatalogFinish[] RawMatches(
        FinishSignal signal,
        IReadOnlyList<CatalogFinish> candidates) =>
        string.IsNullOrWhiteSpace(signal.Raw)
            ? []
            : candidates
                .Where(candidate => candidate.MatchesRaw(signal.Raw))
                .ToArray();

    private static FinishCandidateResolutionResult Unique(
        CatalogFinish candidate,
        decimal confidence,
        IReadOnlyList<string> reasons) =>
        new(
            candidate.ToAlternative(confidence, reasons),
            [candidate.ToAlternative(confidence, reasons)],
            confidence,
            candidate.RequiresReview,
            candidate.RequiresReview ? [FinishResolutionReviewReasons.FinishAmbiguous] : [],
            reasons);

    private static FinishCandidateResolutionResult Ambiguous(
        IReadOnlyList<CatalogFinish> candidates)
    {
        var alternatives = candidates
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .Select(value => value.ToAlternative(
                ProbableConfidence,
                [FinishResolutionReasonCodes.SecondaryAttributesMissing]))
            .ToArray();
        return new(
            null,
            alternatives,
            0m,
            true,
            [FinishResolutionReviewReasons.FinishAmbiguous],
            []);
    }

    private static FinishCandidateResolutionResult Review(
        string reason,
        IReadOnlyList<FinishCandidateAlternative>? alternatives = null) =>
        new(null, alternatives ?? [], 0m, true, [reason], []);

    private sealed record FinishSignal(
        string? Raw,
        string? NormalizedType,
        string? Material,
        string? Color,
        string? Texture,
        string? Process,
        string? CommercialCode,
        bool HasAnySignal,
        bool HasRecognizedSignal,
        bool HasUnrecognizedExplicitColor)
    {
        public static FinishSignal From(FinishCandidateResolutionInput input)
        {
            var joined = Join(
                input.FinishRawDescription,
                input.FinishNormalizedType,
                input.FinishColorRaw,
                input.FinishColorNormalized,
                input.FinishTextureRaw,
                input.FinishTextureNormalized,
                input.FinishExplicitCode);
            var normalized = Normalize(joined);
            var raw = Normalize(input.FinishRawDescription);
            var commercialCode = CommercialCodeFrom(
                Normalize(input.FinishExplicitCode))
                ?? CommercialCodeFrom(raw);
            var normalizedType = TypeFrom(Normalize(input.FinishNormalizedType))
                ?? TypeFrom(normalized);
            var color = ColorFrom(Normalize(input.FinishColorNormalized))
                ?? ColorFrom(Normalize(input.FinishColorRaw))
                ?? ColorFrom(normalized);
            var texture = TextureFrom(Normalize(input.FinishTextureNormalized))
                ?? TextureFrom(Normalize(input.FinishTextureRaw))
                ?? TextureFrom(normalized);
            var process = ProcessFrom(normalized);
            var material = MaterialFrom(normalizedType, normalized);
            var hasExplicitColor = !string.IsNullOrWhiteSpace(
                Join(input.FinishColorRaw, input.FinishColorNormalized));

            return new(
                raw,
                normalizedType,
                material,
                color,
                texture,
                process,
                commercialCode,
                !string.IsNullOrWhiteSpace(joined),
                normalizedType is not null
                    || material is not null
                    || color is not null
                    || texture is not null
                    || process is not null
                    || commercialCode is not null,
                hasExplicitColor && color is null);
        }

        private static string? TypeFrom(string value)
        {
            if (value is "PAINTED" or "PINTADO")
            {
                return "PAINTED";
            }

            if (value is "ANODIZED" or "ANODIZADO")
            {
                return "ANODIZED";
            }

            if (value is "STAINLESS_STEEL" or "INOX"
                || value.Contains("ACERO INOXIDABLE", StringComparison.Ordinal))
            {
                return "STAINLESS_STEEL";
            }

            if (value is "NOT_APPLICABLE" or "N.A" or "N.A.")
            {
                return "NOT_APPLICABLE";
            }

            return null;
        }

        private static string? ColorFrom(string value)
        {
            if (value.Contains("NEGRO", StringComparison.Ordinal)
                || value is "BLACK")
            {
                return "BLACK";
            }

            if (value.Contains("BLANCO", StringComparison.Ordinal)
                || value is "WHITE")
            {
                return "WHITE";
            }

            if (value.Contains("GRIS", StringComparison.Ordinal)
                || value is "GRAY")
            {
                return "GRAY";
            }

            if (value.Contains("CHAMPANA", StringComparison.Ordinal)
                || value.Contains("CHAMPAGNE", StringComparison.Ordinal))
            {
                return "CHAMPAGNE";
            }

            return null;
        }

        private static string? TextureFrom(string value)
        {
            if (value.Contains("MATE", StringComparison.Ordinal)
                || value is "MATTE")
            {
                return "MATTE";
            }

            if (value.Contains("BRILLANTE", StringComparison.Ordinal)
                || value is "GLOSSY")
            {
                return "GLOSSY";
            }

            return null;
        }

        private static string? ProcessFrom(string value)
        {
            if (value.Contains("POLIESTER", StringComparison.Ordinal)
                || value.Contains("POLYESTER", StringComparison.Ordinal)
                || value.Contains("PINTURA", StringComparison.Ordinal)
                || value.Contains("PINTURA AL HORNO", StringComparison.Ordinal))
            {
                return "POLYESTER";
            }

            return null;
        }

        private static string? MaterialFrom(string? normalizedType, string value)
        {
            if (normalizedType == "STAINLESS_STEEL")
            {
                return "STAINLESS_STEEL";
            }

            if (normalizedType is "PAINTED" or "ANODIZED"
                || value.Contains("ALUCOLOR", StringComparison.Ordinal)
                || value.Contains("ALUMINIO", StringComparison.Ordinal))
            {
                return "ALUMINUM";
            }

            return null;
        }

        private static string? CommercialCodeFrom(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var tokens = new[] { "PP13", "PP003", "AN001" };
            return tokens.FirstOrDefault(token =>
                value.Contains(token, StringComparison.Ordinal));
        }
    }

    private sealed record CatalogFinish(
        Guid Id,
        string Code,
        string Name,
        string? NormalizedType,
        string? Color,
        string? Texture,
        string? Process,
        string? CommercialCode,
        string? Material,
        bool RequiresReview)
    {
        public static CatalogFinish From(FinishTypeCatalogReadModel model) =>
            new(
                model.Id,
                model.Code,
                model.Name,
                Normalize(model.NormalizedType),
                Normalize(model.Color),
                Normalize(model.Texture),
                Normalize(model.Process),
                Normalize(model.CommercialCode),
                Normalize(model.Material),
                model.RequiresReview);

        public bool MatchesCommercialCode(string value) =>
            Code.Equals(value, StringComparison.Ordinal)
            || CommercialCode?.Equals(value, StringComparison.Ordinal) == true;

        public bool MatchesNormalizedType(string value) =>
            NormalizedType is null || NormalizedType == value;

        public bool MatchesMaterial(string value) =>
            Material is null || Material == value;

        public bool MatchesColor(string value) =>
            Color is null || Color == value;

        public bool MatchesTexture(string value) =>
            Texture is null || Texture == value;

        public bool MatchesProcess(string value) =>
            Process is null || Process == value;

        public bool MatchesRaw(string raw)
        {
            var normalizedName = Normalize(Name);
            return normalizedName.Contains(raw, StringComparison.Ordinal)
                || raw.Contains(normalizedName, StringComparison.Ordinal)
                || CommercialCode is not null
                    && raw.Contains(CommercialCode, StringComparison.Ordinal);
        }

        public FinishCandidateAlternative ToAlternative(
            decimal confidence,
            IReadOnlyList<string> reasons) =>
            new(Id, Code, Name, confidence, reasons);
    }

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

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }
                continue;
            }

            builder.Append(character);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
