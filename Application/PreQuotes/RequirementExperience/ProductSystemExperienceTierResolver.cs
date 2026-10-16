using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.Common.Abstractions.Catalogs;

namespace Application.PreQuotes.RequirementExperience;

public static partial class ProductSystemExperienceTierResolver
{
    public const string Unknown = "UNKNOWN";

    public static string Resolve(ProductSystemCatalogReadModel system)
    {
        foreach (var text in new[] { system.Name, system.TechnicalName })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var normalized = new string(text.Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character)
                    != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
            var matches = TierPattern().Matches(normalized);
            var tiers = matches.Select(match => match.Groups[1].Value).Distinct().ToArray();
            if (tiers.Contains("PREMIUM")) return "PREMIUM";
            if (tiers.Contains("CLASSIC") || TraditionalPattern().IsMatch(normalized)) return "CLASSIC";
        }
        return Unknown;
    }

    [GeneratedRegex(@"\bLINEA\s+(CLASSIC|PREMIUM)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TierPattern();

    [GeneratedRegex(@"\bTRADICIONAL\b", RegexOptions.CultureInvariant)]
    private static partial Regex TraditionalPattern();
}
