namespace Application.PreQuotes.RequirementExperience;

public static class RequirementExperienceLevel1Resolver
{
    public const string SystemTierClassic = "CLASSIC";
    public const string SystemTierPremium = "PREMIUM";
    public const string GlassFamilyTemplado = "TEMPLADO";
    public const string GlassFamilyLaminado = "LAMINADO";
    public const int RequiredBenefits = 5;

    private static readonly HashSet<string> RequiredBenefitCodes =
    [
        "THERMAL",
        "ACOUSTIC",
        "SECURITY",
        "UV",
        "AESTHETICS"
    ];

    public static RequirementExperienceLevel1Result Resolve(
        IEnumerable<RequirementExperienceLevel1Answer> answers)
    {
        return Resolve(RequirementExperienceCatalog.V3Version, answers);
    }

    public static RequirementExperienceLevel1Result Resolve(
        string catalogVersion,
        IEnumerable<RequirementExperienceLevel1Answer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        string? systemTier = null;
        string? glassFamily = null;
        var answeredBenefits = new HashSet<string>(StringComparer.Ordinal);

        foreach (var answer in answers)
        {
            if (!RequiredBenefitCodes.Contains(answer.BenefitCode))
            {
                continue;
            }

            if (!TryApplyAnswer(catalogVersion, answer, ref systemTier, ref glassFamily))
            {
                continue;
            }

            answeredBenefits.Add(answer.BenefitCode);
        }

        return new RequirementExperienceLevel1Result(
            systemTier,
            glassFamily,
            answeredBenefits.Count == RequiredBenefits,
            answeredBenefits.Count,
            RequiredBenefits);
    }

    private static bool TryApplyAnswer(
        string catalogVersion,
        RequirementExperienceLevel1Answer answer,
        ref string? systemTier,
        ref string? glassFamily)
    {
        if (string.Equals(catalogVersion, RequirementExperienceCatalog.V4Version, StringComparison.Ordinal))
        {
            return TryApplyV4Answer(answer, ref systemTier, ref glassFamily);
        }

        if (!string.Equals(catalogVersion, RequirementExperienceCatalog.V3Version, StringComparison.Ordinal))
        {
            return false;
        }

        return TryApplyV3Answer(answer, ref systemTier, ref glassFamily);
    }

    private static bool TryApplyV3Answer(
        RequirementExperienceLevel1Answer answer,
        ref string? systemTier,
        ref string? glassFamily)
    {
        return answer.BenefitCode switch
        {
            "THERMAL" => ApplyScaledRequirement(answer.OptionCode, "THERMAL", ref systemTier, ref glassFamily),
            "ACOUSTIC" => ApplyScaledRequirement(answer.OptionCode, "ACOUSTIC", ref systemTier, ref glassFamily),
            "SECURITY" => ApplyScaledRequirement(answer.OptionCode, "SECURITY", ref systemTier, ref glassFamily),
            "UV" => ApplyUvRequirement(answer.OptionCode, ref glassFamily),
            "AESTHETICS" => ApplyAestheticsRequirement(answer.OptionCode, ref systemTier),
            _ => false
        };
    }

    private static bool TryApplyV4Answer(
        RequirementExperienceLevel1Answer answer,
        ref string? systemTier,
        ref string? glassFamily)
    {
        return answer.BenefitCode switch
        {
            "THERMAL" => ApplyV4ScaledRequirement(answer.OptionCode, "THERMAL", ref systemTier, ref glassFamily),
            "ACOUSTIC" => ApplyV4ScaledRequirement(answer.OptionCode, "ACOUSTIC", ref systemTier, ref glassFamily),
            "SECURITY" => ApplyV4ScaledRequirement(answer.OptionCode, "SECURITY", ref systemTier, ref glassFamily),
            "UV" => ApplyUvRequirement(answer.OptionCode, ref glassFamily),
            "AESTHETICS" => ApplyV4AestheticsRequirement(answer.OptionCode, ref systemTier),
            _ => false
        };
    }

    private static bool ApplyScaledRequirement(
        string optionCode,
        string prefix,
        ref string? systemTier,
        ref string? glassFamily)
    {
        if (!TryGetScale(optionCode, prefix, out var scale))
        {
            return false;
        }

        if (scale <= 3)
        {
            UpgradeSystemTier(ref systemTier, SystemTierClassic);
            UpgradeGlassFamily(ref glassFamily, GlassFamilyTemplado);
            return true;
        }

        UpgradeSystemTier(ref systemTier, SystemTierPremium);
        UpgradeGlassFamily(ref glassFamily, GlassFamilyLaminado);
        return true;
    }

    private static bool ApplyUvRequirement(string optionCode, ref string? glassFamily)
    {
        if (string.Equals(optionCode, "UV_NO", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(optionCode, "UV_YES", StringComparison.Ordinal))
        {
            return false;
        }

        UpgradeGlassFamily(ref glassFamily, GlassFamilyLaminado);
        return true;
    }

    private static bool ApplyV4ScaledRequirement(
        string optionCode,
        string prefix,
        ref string? systemTier,
        ref string? glassFamily)
    {
        if (!TryGetSemanticLevel(optionCode, prefix, out var level))
        {
            return false;
        }

        if (level == RequirementExperienceSemanticLevel.Low)
        {
            UpgradeSystemTier(ref systemTier, SystemTierClassic);
            UpgradeGlassFamily(ref glassFamily, GlassFamilyTemplado);
            return true;
        }

        UpgradeSystemTier(ref systemTier, SystemTierPremium);
        UpgradeGlassFamily(ref glassFamily, GlassFamilyLaminado);
        return true;
    }

    private static bool ApplyV4AestheticsRequirement(string optionCode, ref string? systemTier)
    {
        if (!TryGetSemanticLevel(optionCode, "AESTHETICS", out var level))
        {
            return false;
        }

        UpgradeSystemTier(
            ref systemTier,
            level == RequirementExperienceSemanticLevel.Low
                ? SystemTierClassic
                : SystemTierPremium);
        return true;
    }

    private static bool ApplyAestheticsRequirement(string optionCode, ref string? systemTier)
    {
        if (!TryGetScale(optionCode, "AESTHETICS", out var scale))
        {
            return false;
        }

        UpgradeSystemTier(ref systemTier, scale <= 3 ? SystemTierClassic : SystemTierPremium);
        return true;
    }

    private static bool TryGetScale(string optionCode, string prefix, out int scale)
    {
        scale = 0;
        var expectedPrefix = prefix + "_";
        if (!optionCode.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(optionCode[expectedPrefix.Length..], out scale)
            && scale is >= 1 and <= 5;
    }

    private static bool TryGetSemanticLevel(
        string optionCode,
        string prefix,
        out RequirementExperienceSemanticLevel level)
    {
        level = RequirementExperienceSemanticLevel.Unknown;
        var expectedPrefix = prefix + "_";
        if (!optionCode.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = optionCode[expectedPrefix.Length..];
        if (string.Equals(suffix, "LOW", StringComparison.Ordinal))
        {
            level = RequirementExperienceSemanticLevel.Low;
            return true;
        }

        if (string.Equals(suffix, "MEDIUM", StringComparison.Ordinal))
        {
            level = RequirementExperienceSemanticLevel.Medium;
            return true;
        }

        if (string.Equals(suffix, "HIGH", StringComparison.Ordinal))
        {
            level = RequirementExperienceSemanticLevel.High;
            return true;
        }

        return false;
    }

    private static void UpgradeSystemTier(ref string? current, string requested)
    {
        if (string.Equals(current, SystemTierPremium, StringComparison.Ordinal))
        {
            return;
        }

        current = requested;
    }

    private static void UpgradeGlassFamily(ref string? current, string requested)
    {
        if (string.Equals(current, GlassFamilyLaminado, StringComparison.Ordinal))
        {
            return;
        }

        current = requested;
    }
}

public sealed record RequirementExperienceLevel1Answer(
    string BenefitCode,
    string OptionCode);

public sealed record RequirementExperienceLevel1Result(
    string? SystemTier,
    string? GlassFamily,
    bool IsComplete,
    int AnsweredBenefits,
    int RequiredBenefits);

internal enum RequirementExperienceSemanticLevel
{
    Unknown,
    Low,
    Medium,
    High
}
