using Application.PreQuotes.RequirementExperience;
using Xunit;

namespace Tests.Application.PreQuotes;

public sealed class RequirementExperienceLevel1ResolverTests
{
    [Fact]
    public void ResolveV4_WithAllLowAndUvNo_ReturnsClassicTempladoComplete()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_LOW"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_LOW"));

        Assert.Equal("CLASSIC", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.True(result.IsComplete);
        Assert.Equal(5, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void ResolveV4_WithUvYes_UpgradesGlassToLaminado()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_LOW"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_YES"),
            ("AESTHETICS", "AESTHETICS_LOW"));

        Assert.Equal("CLASSIC", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ResolveV4_WithThermalMedium_ReturnsPremiumLaminado()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_MEDIUM"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_LOW"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ResolveV4_WithThermalHigh_ReturnsPremiumLaminado()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_HIGH"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_LOW"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ResolveV4_WithAestheticsMedium_ReturnsPremiumTemplado()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_LOW"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_MEDIUM"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ResolveV4_WithAestheticsHigh_ReturnsPremiumTemplado()
    {
        var result = ResolveV4(
            ("THERMAL", "THERMAL_LOW"),
            ("ACOUSTIC", "ACOUSTIC_LOW"),
            ("SECURITY", "SECURITY_LOW"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_HIGH"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ResolveV4_WithPartialAestheticsHigh_ReturnsPremiumWithoutGlassFamily()
    {
        var result = ResolveV4(("AESTHETICS", "AESTHETICS_HIGH"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Null(result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void ResolveV4_WithPartialUvYes_ReturnsLaminadoWithoutSystemTier()
    {
        var result = ResolveV4(("UV", "UV_YES"));

        Assert.Null(result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void ResolveV4_WithPartialUvNo_ReturnsIncompleteWithoutFamilies()
    {
        var result = ResolveV4(("UV", "UV_NO"));

        Assert.Null(result.SystemTier);
        Assert.Null(result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void ResolveV4_WithNoAnswersReturnsEmptyIncompleteResult()
    {
        var result = ResolveV4();

        Assert.Null(result.SystemTier);
        Assert.Null(result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void Resolve_WithAllMidLevelAnswers_ReturnsClassicTempladoComplete()
    {
        var result = Resolve(
            ("THERMAL", "THERMAL_3"),
            ("ACOUSTIC", "ACOUSTIC_3"),
            ("SECURITY", "SECURITY_2"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_3"));

        Assert.Equal("CLASSIC", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.True(result.IsComplete);
        Assert.Equal(5, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    [Fact]
    public void Resolve_WithUvYesOnlyUpgradesGlassToLaminado()
    {
        var result = Resolve(
            ("THERMAL", "THERMAL_3"),
            ("ACOUSTIC", "ACOUSTIC_2"),
            ("SECURITY", "SECURITY_2"),
            ("UV", "UV_YES"),
            ("AESTHETICS", "AESTHETICS_3"));

        Assert.Equal("CLASSIC", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Resolve_WithHighThermalReturnsPremiumLaminado()
    {
        var result = Resolve(
            ("THERMAL", "THERMAL_5"),
            ("ACOUSTIC", "ACOUSTIC_3"),
            ("SECURITY", "SECURITY_2"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_3"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Resolve_WithHighAestheticsOnlyUpgradesSystem()
    {
        var result = Resolve(
            ("THERMAL", "THERMAL_2"),
            ("ACOUSTIC", "ACOUSTIC_2"),
            ("SECURITY", "SECURITY_2"),
            ("UV", "UV_NO"),
            ("AESTHETICS", "AESTHETICS_5"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Resolve_WithHighAestheticsAndUvYesReturnsPremiumLaminado()
    {
        var result = Resolve(
            ("THERMAL", "THERMAL_2"),
            ("ACOUSTIC", "ACOUSTIC_2"),
            ("SECURITY", "SECURITY_2"),
            ("UV", "UV_YES"),
            ("AESTHETICS", "AESTHETICS_5"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Resolve_WithPartialThermalDoesNotAssumeMissingHigherNeeds()
    {
        var result = Resolve(("THERMAL", "THERMAL_2"));

        Assert.Equal("CLASSIC", result.SystemTier);
        Assert.Equal("TEMPLADO", result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
    }

    [Fact]
    public void Resolve_WithPartialHighAestheticsReturnsPremiumWithoutGlassFamily()
    {
        var result = Resolve(("AESTHETICS", "AESTHETICS_5"));

        Assert.Equal("PREMIUM", result.SystemTier);
        Assert.Null(result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
    }

    [Fact]
    public void Resolve_WithPartialUvYesReturnsLaminadoWithoutSystemTier()
    {
        var result = Resolve(("UV", "UV_YES"));

        Assert.Null(result.SystemTier);
        Assert.Equal("LAMINADO", result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.AnsweredBenefits);
    }

    [Fact]
    public void Resolve_WithNoAnswersReturnsEmptyIncompleteResult()
    {
        var result = Resolve();

        Assert.Null(result.SystemTier);
        Assert.Null(result.GlassFamily);
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.AnsweredBenefits);
        Assert.Equal(5, result.RequiredBenefits);
    }

    private static RequirementExperienceLevel1Result Resolve(
        params (string BenefitCode, string OptionCode)[] answers)
    {
        return RequirementExperienceLevel1Resolver.Resolve(
            answers.Select(answer => new RequirementExperienceLevel1Answer(
                answer.BenefitCode,
                answer.OptionCode)));
    }

    private static RequirementExperienceLevel1Result ResolveV4(
        params (string BenefitCode, string OptionCode)[] answers)
    {
        return RequirementExperienceLevel1Resolver.Resolve(
            RequirementExperienceCatalog.V4Version,
            answers.Select(answer => new RequirementExperienceLevel1Answer(
                answer.BenefitCode,
                answer.OptionCode)));
    }
}
