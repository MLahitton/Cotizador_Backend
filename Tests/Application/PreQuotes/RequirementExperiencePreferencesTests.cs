using Application.PreQuotes.RequirementExperience;
using Application.Projects.GetProjectDiagnosis;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RequirementExperiencePreferencesTests
{
    private static ProjectDiagnosisSource Source(string json) => new("fixture", "SNG_WEB", "sng-public-experience-v1", json);

    [Theory]
    [InlineData("1", "LOW")]
    [InlineData("2", "LOW")]
    [InlineData("3", "MEDIUM")]
    [InlineData("4", "HIGH")]
    [InlineData("5", "HIGH")]
    [InlineData("0", null)]
    [InlineData("6", null)]
    [InlineData("2.5", null)]
    [InlineData("\"5\"", null)]
    [InlineData("null", null)]
    public void Scales(string value, string? expected)
    {
        var source = Source($$"""{"acustico":{{value}},"termico":{{value}},"seguridad":{{value}},"estetica":{{value}}}""");
        var answers = RequirementExperiencePreferences.FromSources([source]);
        Assert.Equal(expected is null ? 0 : 4, answers.Count);
        Assert.All(answers, x => Assert.Equal(x.BenefitCode + "_" + expected, x.OptionCode));
    }

    [Theory]
    [InlineData("\"si\"", "UV_YES")]
    [InlineData("\"ns\"", "UV_YES")]
    [InlineData("\"no\"", "UV_NO")]
    [InlineData("\"unknown\"", null)]
    [InlineData("true", null)]
    [InlineData("null", null)]
    public void Uv(string value, string? expected)
    {
        var answers = RequirementExperiencePreferences.FromSources([Source("{\"uv\":" + value + "}")]);
        if (expected is null) Assert.Empty(answers);
        else Assert.Equal(expected, Assert.Single(answers).OptionCode);
    }

    [Fact]
    public void AbsentAmbiguousInvalidAndUnsupportedRemainPending()
    {
        Assert.Empty(RequirementExperiencePreferences.FromSources([]));
        var source = Source("{\"acustico\":5}");
        Assert.Empty(RequirementExperiencePreferences.FromSources([source, source]));
        Assert.Empty(RequirementExperiencePreferences.FromSources([source with { SchemaVersion = "future" }]));
        Assert.Empty(RequirementExperiencePreferences.FromSources([Source("{\"acustico\":5,\"acustico\":1}")]));
        Assert.Empty(RequirementExperiencePreferences.FromSources([Source("{}")]));
        Assert.Empty(RequirementExperiencePreferences.FromSources([Source("{\"estetica\":5,\"estetica\":1}")]));
    }

    [Fact]
    public void FivePrioritiesAreInheritedButPartialSavedDraftIsNotCompleted()
    {
        var initial = RequirementExperiencePreferences.FromSources([
            Source("{\"acustico\":5,\"termico\":4,\"seguridad\":5,\"estetica\":5,\"uv\":\"ns\"}")]);
        var item = Guid.NewGuid();
        var inherited = RequirementExperiencePreferences.Resolve(item, null, initial);
        Assert.Equal(5, inherited.Answers.Count);
        Assert.True(inherited.Level1Resolution!.IsComplete);
        Assert.Equal("AESTHETICS_HIGH", inherited.Answers.Single(x => x.BenefitCode == "AESTHETICS").OptionCode);
        Assert.False(inherited.HasSavedDraft);
        var saved = RequirementItemExperienceDraft.Create(Guid.NewGuid(), item, RequirementExperienceCatalog.V4Version,
            null, [new("ACOUSTIC", "ACOUSTIC_MEDIUM")], Guid.NewGuid(), DateTimeOffset.UtcNow);
        var actual = RequirementExperiencePreferences.Resolve(item, saved, initial);
        Assert.Equal("ACOUSTIC_MEDIUM", Assert.Single(actual.Answers).OptionCode);
        Assert.DoesNotContain(actual.Answers, x => x.BenefitCode == "AESTHETICS");
        Assert.False(actual.Level1Resolution!.IsComplete);
        Assert.True(actual.HasSavedDraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDraftHasCompletePrecedence(bool empty)
    {
        var item = Guid.NewGuid();
        var initial = RequirementExperiencePreferences.FromSources([Source("{\"acustico\":5}")]);
        var saved = RequirementItemExperienceDraft.Create(Guid.NewGuid(), item, RequirementExperienceCatalog.V3Version,
            null, empty ? [] : [new("ACOUSTIC", "ACOUSTIC_3")], Guid.NewGuid(), DateTimeOffset.UtcNow);
        var actual = RequirementExperiencePreferences.Resolve(item, saved, initial);
        Assert.True(actual.HasSavedDraft);
        Assert.Equal("SAVED", actual.PreferenceOrigin);
        Assert.Equal(RequirementExperienceCatalog.V3Version, actual.CatalogVersion);
        Assert.Equal(empty ? 0 : 1, actual.Answers.Count);
        Assert.DoesNotContain(actual.Answers, x => x.OptionCode == "ACOUSTIC_HIGH");
        var other = RequirementExperiencePreferences.Resolve(Guid.NewGuid(), null, initial);
        Assert.False(other.HasSavedDraft);
        Assert.Equal("EXPERIENCE", other.PreferenceOrigin);
        Assert.Equal("ACOUSTIC_HIGH", Assert.Single(other.Answers).OptionCode);
    }
}
