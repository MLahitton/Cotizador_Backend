using System.Globalization;
using System.Text.RegularExpressions;
using Application.Common.Abstractions.Proposals;

namespace Application.Proposals.FpPro.Experimental;

public interface IFpProModuleInferenceService
{
    FpProModuleInferenceResult Infer(FpProPreviewItemData item);
}

public sealed class FpProModuleInferenceService : IFpProModuleInferenceService
{
    private static readonly Regex ProfileKeyRegex = new(
        @"(?<key>[A-Z0-9][A-Z0-9._-]{1,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LengthRegex = new(
        @"(?<value>\d+(?:[.,]\d+)?)\s*(?:m|mt|mts)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly FpProModuleInferenceOptions _options;

    public FpProModuleInferenceService()
        : this(FpProModuleInferenceOptions.Default)
    {
    }

    public FpProModuleInferenceService(FpProModuleInferenceOptions options)
    {
        _options = options;
    }

    public FpProModuleInferenceResult Infer(FpProPreviewItemData item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Infer(FpProModuleInferenceInput.FromPreviewItem(item));
    }

    public FpProModuleInferenceResult Infer(FpProModuleInferenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var warnings = new List<string>();
        var quantity = input.Quantity <= 0 ? 1 : input.Quantity;
        if (input.Quantity <= 0)
        {
            warnings.Add("QUANTITY_MISSING_OR_INVALID_ASSUMED_1");
        }

        var widthM = input.WidthMeters;
        var heightM = input.HeightMeters;
        if (widthM <= 0 || heightM <= 0)
        {
            warnings.Add("DIMENSIONS_MISSING_OR_INVALID");
        }

        var glassModules = CalculateGlassModules(input, quantity, warnings);
        var frameGroups = CalculateFrameGroups(input, quantity, widthM, heightM, glassModules, warnings);
        var frameModules = frameGroups.Sum(value => value.ModuleCount);
        var rawModules = glassModules + frameModules;
        var finalModules = Math.Max(_options.MinimumModules, rawModules);
        var requiresManualModule = warnings.Any(value => value.Contains("AMBIGUOUS", StringComparison.OrdinalIgnoreCase))
            || frameGroups.SelectMany(value => value.Warnings).Any(value => value.Contains("AMBIGUOUS", StringComparison.OrdinalIgnoreCase))
            || warnings.Contains("DIMENSIONS_MISSING_OR_INVALID");

        var confidence = CalculateConfidence(frameGroups, warnings, input.ProfileUsages.Count);

        return new FpProModuleInferenceResult(
            input.ItemNumber,
            glassModules,
            frameGroups,
            frameModules,
            rawModules,
            finalModules,
            confidence,
            warnings,
            requiresManualModule);
    }

    private static int CalculateGlassModules(
        FpProModuleInferenceInput input,
        int quantity,
        ICollection<string> warnings)
    {
        var glassPieceCount = input.GlassPanes.Sum(value => Math.Max(0, value.Quantity));
        if (glassPieceCount <= 0)
        {
            warnings.Add("GLASS_PANES_NOT_DETECTED");
            return 0;
        }

        var perUnit = glassPieceCount / (decimal)quantity;
        if (glassPieceCount % quantity != 0)
        {
            warnings.Add("GLASS_PANES_NOT_DIVISIBLE_BY_QUANTITY");
        }

        return (int)Math.Ceiling(perUnit);
    }

    private IReadOnlyList<FpProModuleFrameGroupResult> CalculateFrameGroups(
        FpProModuleInferenceInput input,
        int quantity,
        decimal widthM,
        decimal heightM,
        int glassModules,
        ICollection<string> warnings)
    {
        var categorizedProfiles = input.ProfileUsages
            .Select(value => value with { Category = ClassifyProfile(value.ProfileKey, value.Description) })
            .ToArray();
        var frameProfiles = input.ProfileUsages
            .Select(value => value with { Category = ClassifyProfile(value.ProfileKey, value.Description) })
            .Where(value => value.Category is FpProProfileCategory.Frame or FpProProfileCategory.LeafFrame)
            .GroupBy(value => NormalizeProfileKey(value.ProfileKey))
            .ToArray();

        if (frameProfiles.Length == 0)
        {
            var tubularProfiles = categorizedProfiles
                .Where(value => ContainsAny(NormalizeForMatching(string.Join(' ', value.ProfileKey, value.Description)), "TUBULARES", "TUBULAR"))
                .ToArray();
            if (tubularProfiles.Length > 0)
            {
                return
                [
                    new FpProModuleFrameGroupResult(
                        "TUBULARES",
                        tubularProfiles.Select(value => value.Description).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        FpProProfileCategory.Frame,
                        null,
                        null,
                        null,
                        1,
                        ["TUBULAR_FRAME_FALLBACK"])
                ];
            }

            warnings.Add("FRAME_PROFILES_NOT_DETECTED");
            return [];
        }

        var results = new List<FpProModuleFrameGroupResult>(frameProfiles.Length);
        foreach (var group in frameProfiles)
        {
            var usages = group.ToArray();
            var totalLength = usages
                .Where(value => value.LengthMetersTotal.HasValue)
                .Sum(value => value.LengthMetersTotal.GetValueOrDefault());

            var category = usages.Select(value => value.Category).First();
            var lengthPerUnit = totalLength > 0 ? totalLength / quantity : (decimal?)null;
            var estimatedFrameCount = EstimateFrameCount(lengthPerUnit, widthM, heightM);
            var groupWarnings = new List<string>();
            var modules = 0;

            if (lengthPerUnit is null)
            {
                groupWarnings.Add("PROFILE_LENGTH_MISSING");
                modules = widthM > 0 && frameProfiles.Length == 1
                    ? Math.Max(1, (int)Math.Ceiling(widthM / _options.ProfileBarLengthMeters))
                    : 1;
            }
            else if (estimatedFrameCount is null)
            {
                groupWarnings.Add("FRAME_LENGTH_AMBIGUOUS");
            }
            else
            {
                modules = estimatedFrameCount.Value * Math.Max(1, (int)Math.Ceiling(widthM / _options.ProfileBarLengthMeters));
            }

            results.Add(new FpProModuleFrameGroupResult(
                group.Key,
                usages.Select(value => value.Description).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                category,
                totalLength > 0 ? totalLength : null,
                lengthPerUnit,
                estimatedFrameCount,
                modules,
                groupWarnings));
        }

        ApplyFrameGroupHeuristics(results, categorizedProfiles, glassModules);

        return results;
    }

    private static void ApplyFrameGroupHeuristics(
        IList<FpProModuleFrameGroupResult> frameGroups,
        IReadOnlyList<FpProModuleProfileUsage> profiles,
        int glassModules)
    {
        if (glassModules <= 0 || frameGroups.Count == 0)
        {
            return;
        }

        var hasSerie35 = profiles.Any(value => ContainsAny(NormalizeForMatching(string.Join(' ', value.ProfileKey, value.Description)), "SERIE35"));
        var hasSuperior = profiles.Any(value => ContainsAny(NormalizeForMatching(string.Join(' ', value.ProfileKey, value.Description)), "SUPERIOR"));
        if (!hasSerie35)
        {
            return;
        }

        var targetFrameModules = hasSuperior
            ? Math.Max(frameGroups.Sum(value => value.ModuleCount), glassModules * 2)
            : Math.Max(frameGroups.Sum(value => value.ModuleCount), glassModules);
        var missing = targetFrameModules - frameGroups.Sum(value => value.ModuleCount);
        if (missing <= 0)
        {
            return;
        }

        var targetIndex = frameGroups.ToList().FindIndex(value => value.ProfileKey.Contains("SERIE35", StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            targetIndex = 0;
        }

        var current = frameGroups[targetIndex];
        frameGroups[targetIndex] = current with
        {
            ModuleCount = current.ModuleCount + missing,
            Warnings = current.Warnings.Concat(["FRAME_MODULES_ALIGNED_WITH_GLASS_PANES"]).ToArray()
        };
    }

    private int? EstimateFrameCount(decimal? lengthPerUnit, decimal widthM, decimal heightM)
    {
        if (lengthPerUnit is null || widthM <= 0 || heightM <= 0)
        {
            return null;
        }

        var singleFrameLength = 2 * (widthM + heightM);
        if (IsClose(lengthPerUnit.Value, singleFrameLength))
        {
            return 1;
        }

        var estimated = (lengthPerUnit.Value - (2 * widthM)) / (2 * heightM);
        var rounded = (int)Math.Round(estimated, MidpointRounding.AwayFromZero);
        if (rounded < 1)
        {
            return null;
        }

        var expectedLength = (2 * widthM) + (2 * rounded * heightM);
        return IsClose(lengthPerUnit.Value, expectedLength) ? rounded : null;
    }

    private bool IsClose(decimal actual, decimal expected)
    {
        var difference = Math.Abs(actual - expected);
        var relativeTolerance = Math.Abs(expected) * (_options.RelativeTolerancePercent / 100m);
        return difference <= _options.AbsoluteToleranceMeters || difference <= relativeTolerance;
    }

    private static int CalculateGlassQuantity(FpProGlassPaneData glass)
    {
        return glass.Quantity is > 0 ? glass.Quantity.Value : 1;
    }

    private static FpProModuleInferenceConfidence CalculateConfidence(
        IReadOnlyList<FpProModuleFrameGroupResult> frameGroups,
        IReadOnlyCollection<string> warnings,
        int profileUsageCount)
    {
        if (warnings.Any(value => value.Contains("AMBIGUOUS", StringComparison.OrdinalIgnoreCase)))
        {
            return FpProModuleInferenceConfidence.Ambiguous;
        }

        if (frameGroups.SelectMany(value => value.Warnings).Any(value => value.Contains("AMBIGUOUS", StringComparison.OrdinalIgnoreCase)))
        {
            return FpProModuleInferenceConfidence.Ambiguous;
        }

        if (profileUsageCount == 0 || warnings.Contains("FRAME_PROFILES_NOT_DETECTED"))
        {
            return FpProModuleInferenceConfidence.Low;
        }

        if (frameGroups.Any(value => value.Warnings.Contains("PROFILE_LENGTH_MISSING")))
        {
            return FpProModuleInferenceConfidence.Medium;
        }

        return FpProModuleInferenceConfidence.High;
    }

    public static FpProProfileCategory ClassifyProfile(string? profileKey, string? description)
    {
        var text = NormalizeForMatching(string.Join(' ', profileKey, description));

        if (ContainsAny(text, "PISAVIDRIO", "PISA VIDRIO", "JUNQUILLO"))
        {
            return FpProProfileCategory.GlazingBead;
        }

        if (ContainsAny(text, "ADAPTADOR", "ENGANCHE"))
        {
            return FpProProfileCategory.Adapter;
        }

        if (ContainsAny(text, "MULLION", "DIVISOR", "T103", "ALFAJIA", "ALFAJIA"))
        {
            return FpProProfileCategory.Divider;
        }

        if (ContainsAny(text, "MARCO", "KONCEPT", "SUPERIOR", "SERIE"))
        {
            return FpProProfileCategory.Frame;
        }

        if (ContainsAny(text, "NAVE"))
        {
            return FpProProfileCategory.LeafFrame;
        }

        return FpProProfileCategory.Other;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeProfileKey(string? value)
    {
        var normalized = NormalizeForMatching(value);
        return normalized.Length == 0 ? "UNKNOWN_PROFILE" : normalized;
    }

    private static string NormalizeForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToUpperInvariant()
            .Replace('Á', 'A')
            .Replace('É', 'E')
            .Replace('Í', 'I')
            .Replace('Ó', 'O')
            .Replace('Ú', 'U');
    }

    internal static FpProModuleProfileUsage ParseProfileUsage(string description)
    {
        var key = ProfileKeyRegex.Match(description).Groups["key"].Value;
        var lengthMatch = LengthRegex.Matches(description).LastOrDefault();
        var length = lengthMatch is null
            ? null
            : ParseDecimal(lengthMatch.Groups["value"].Value);

        return new FpProModuleProfileUsage(
            string.IsNullOrWhiteSpace(key) ? description : key,
            description,
            length,
            FpProProfileCategory.Other);
    }

    private static decimal? ParseDecimal(string value)
    {
        var normalized = value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static IReadOnlyList<FpProModuleGlassPaneInput> MapGlass(FpProPreviewItemData item)
    {
        return item.Glass.Select(value => new FpProModuleGlassPaneInput(CalculateGlassQuantity(value))).ToArray();
    }

    public static IReadOnlyList<FpProModuleProfileUsage> MapProfiles(FpProPreviewItemData item)
    {
        if (item.TechnicalProfiles.Count > 0)
        {
            return item.TechnicalProfiles
                .Select(value => new FpProModuleProfileUsage(
                    value.Code,
                    value.Description,
                    value.UnitLengthMeters ?? value.TotalLengthMeters,
                    FpProProfileCategory.Other))
                .ToArray();
        }

        if (item.TechnicalProfileDescriptions.Count > 0)
        {
            return item.TechnicalProfileDescriptions.Select(ParseProfileUsage).ToArray();
        }

        return item.FpProProfiles
            .Select(value => new FpProModuleProfileUsage(value, value, null, FpProProfileCategory.Other))
            .ToArray();
    }
}

public sealed record FpProModuleInferenceOptions(
    int MinimumModules,
    decimal ProfileBarLengthMeters,
    decimal AbsoluteToleranceMeters,
    decimal RelativeTolerancePercent)
{
    public static FpProModuleInferenceOptions Default { get; } = new(3, 6m, 0.15m, 8m);
}

public sealed record FpProModuleInferenceInput(
    string ItemNumber,
    decimal WidthMeters,
    decimal HeightMeters,
    int Quantity,
    IReadOnlyList<FpProModuleGlassPaneInput> GlassPanes,
    IReadOnlyList<FpProModuleProfileUsage> ProfileUsages)
{
    public static FpProModuleInferenceInput FromPreviewItem(FpProPreviewItemData item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new FpProModuleInferenceInput(
            item.ItemNumber,
            item.WidthM.GetValueOrDefault(),
            item.HeightM.GetValueOrDefault(),
            item.Quantity.GetValueOrDefault(1),
            FpProModuleInferenceService.MapGlass(item),
            FpProModuleInferenceService.MapProfiles(item));
    }
}

public sealed record FpProModuleGlassPaneInput(int Quantity);

public sealed record FpProModuleProfileUsage(
    string ProfileKey,
    string Description,
    decimal? LengthMetersTotal,
    FpProProfileCategory Category);

public sealed record FpProModuleFrameGroupResult(
    string ProfileKey,
    IReadOnlyList<string> Descriptions,
    FpProProfileCategory Category,
    decimal? TotalLengthMeters,
    decimal? LengthMetersPerUnit,
    int? EstimatedIndependentFrameCount,
    int ModuleCount,
    IReadOnlyList<string> Warnings);

public sealed record FpProModuleInferenceResult(
    string ItemNumber,
    int GlassModules,
    IReadOnlyList<FpProModuleFrameGroupResult> FrameGroups,
    int FrameModules,
    int RawModules,
    int FinalModules,
    FpProModuleInferenceConfidence Confidence,
    IReadOnlyList<string> Warnings,
    bool RequiresManualModule);

public enum FpProProfileCategory
{
    Frame,
    LeafFrame,
    Divider,
    GlazingBead,
    Adapter,
    Other
}

public enum FpProModuleInferenceConfidence
{
    High,
    Medium,
    Low,
    Ambiguous
}
