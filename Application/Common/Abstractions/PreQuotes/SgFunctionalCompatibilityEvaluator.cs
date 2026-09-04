using Application.Common.Abstractions.Catalogs;
using Domain.PreQuotes;
using System.Globalization;
using System.Text;

namespace Application.Common.Abstractions.PreQuotes;

public static class SgFunctionalCompatibilityEvaluator
{
    public const string FunctionalTypeCompatibilityRule =
        "FUNCTIONAL_TYPE_COMPATIBILITY";
    public const string TechnicalProposalFunctionalTypeMismatch =
        "TECHNICAL_PROPOSAL_FUNCTIONAL_TYPE_MISMATCH";
    public const string TechnicalProposalFunctionalTypeUnknown =
        "TECHNICAL_PROPOSAL_FUNCTIONAL_TYPE_UNKNOWN";

    public static SgFunctionalCompatibilityResult Evaluate(
        SgTechnicalSelectionInput input,
        ProductSystemCatalogReadModel system)
    {
        var expected = EffectiveFunctionalType(input);
        var actual = Code(system.FunctionalType);
        if (expected is null || actual is null)
        {
            return SgFunctionalCompatibilityResult.Unknown(expected, actual);
        }

        return expected == actual
            ? SgFunctionalCompatibilityResult.Compatible(expected, actual)
            : SgFunctionalCompatibilityResult.Incompatible(expected, actual);
    }

    public static SgFunctionalCompatibilityResult Evaluate(
        RequirementTechnicalProposalItem item,
        ProductSystemCatalogReadModel system) =>
        Evaluate(ToSelectionInput(item), system);

    public static SgTechnicalSelectionInput ToSelectionInput(
        RequirementTechnicalProposalItem item)
    {
        var extracted = item.ExtractedItem;
        return new SgTechnicalSelectionInput(
            extracted?.FunctionalType ?? FunctionalTypeFromElementType(item.ElementType),
            extracted?.Operation,
            item.EffectiveWidthMillimeters,
            item.EffectiveHeightMillimeters,
            extracted?.AreaSquareMeters,
            null,
            null,
            null,
            extracted?.Modulation,
            extracted?.OpeningDirection,
            extracted?.SpecialFeatures ?? [],
            extracted?.GeometryType,
            null,
            extracted?.RequestedSystemRaw ?? extracted?.RequestedProfileRaw,
            null,
            null,
            extracted?.Description ?? item.Description,
            null,
            false,
            null);
    }

    public static string? EffectiveFunctionalType(
        SgTechnicalSelectionInput input)
    {
        var functionalType = Code(input.FunctionalType);
        var operation = Code(input.Operation);
        var effectiveHeight = EffectiveFunctionalHeight(input);
        if (functionalType == "WINDOW"
            && effectiveHeight > 2600)
        {
            return operation switch
            {
                "SLIDING" => "SLIDING_DOOR",
                "SWING" => "SWING_DOOR",
                _ => "DOOR"
            };
        }

        if (functionalType == "SLIDING_WINDOW"
            && effectiveHeight > 2600)
        {
            return "SLIDING_DOOR";
        }

        if (functionalType == "WINDOW")
        {
            return operation switch
            {
                "FIXED" => "FIXED",
                "SLIDING" => "SLIDING_WINDOW",
                _ => functionalType
            };
        }

        return functionalType;
    }

    public static IReadOnlyList<string> FunctionalResolutionReasons(
        SgTechnicalSelectionInput input) =>
        Code(input.FunctionalType) is "WINDOW" or "SLIDING_WINDOW"
        && EffectiveFunctionalHeight(input) > 2600
            ? [SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor]
            : [];

    public static string NormalizeTechnicalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            if (char.IsLetterOrDigit(upper))
            {
                builder.Append(upper);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static string? Code(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeTechnicalText(value);
        return normalized switch
        {
            "BATHROOM DIVISION" or "BATHROOM_DIVISION" => "SHOWER_DIVISION",
            "LOUVER" => "GRILLE",
            "VENTANA CORREDERA" or "CORREDERA 02 HOJAS" => "SLIDING_WINDOW",
            "VENTANA FIJA" or "FIJA SIMPLE" => "FIXED",
            "VENTANA DE ESQUINA" => "WINDOW",
            "CORREDERA" => "SLIDING",
            "FIJA" => "FIXED",
            _ => value.Trim().ToUpperInvariant()
        };
    }

    private static int? EffectiveFunctionalHeight(
        SgTechnicalSelectionInput input)
    {
        if (input.PrimaryComponentHeightMillimeters is > 0)
        {
            return input.PrimaryComponentHeightMillimeters;
        }

        return input.HasCompositeGeometry
            ? null
            : input.HeightMillimeters;
    }

    private static string? FunctionalTypeFromElementType(
        StructuredElementType elementType) =>
        elementType switch
        {
            StructuredElementType.ShowerDivision => "SHOWER_DIVISION",
            StructuredElementType.Skylight => "SKYLIGHT",
            _ => null
        };
}

public sealed record SgFunctionalCompatibilityResult(
    SgFunctionalCompatibilityState State,
    string? ExpectedFunctionalType,
    string? ActualFunctionalType,
    string? ReasonCode)
{
    public bool IsCompatible =>
        State == SgFunctionalCompatibilityState.Compatible;

    public bool IsIncompatible =>
        State == SgFunctionalCompatibilityState.Incompatible;

    public static SgFunctionalCompatibilityResult Compatible(
        string expectedFunctionalType,
        string actualFunctionalType) =>
        new(
            SgFunctionalCompatibilityState.Compatible,
            expectedFunctionalType,
            actualFunctionalType,
            SgFunctionalCompatibilityEvaluator.FunctionalTypeCompatibilityRule);

    public static SgFunctionalCompatibilityResult Incompatible(
        string expectedFunctionalType,
        string actualFunctionalType) =>
        new(
            SgFunctionalCompatibilityState.Incompatible,
            expectedFunctionalType,
            actualFunctionalType,
            SgFunctionalCompatibilityEvaluator.TechnicalProposalFunctionalTypeMismatch);

    public static SgFunctionalCompatibilityResult Unknown(
        string? expectedFunctionalType,
        string? actualFunctionalType) =>
        new(
            SgFunctionalCompatibilityState.Unknown,
            expectedFunctionalType,
            actualFunctionalType,
            SgFunctionalCompatibilityEvaluator.TechnicalProposalFunctionalTypeUnknown);
}

public enum SgFunctionalCompatibilityState
{
    Compatible = 1,
    Incompatible,
    Unknown
}
