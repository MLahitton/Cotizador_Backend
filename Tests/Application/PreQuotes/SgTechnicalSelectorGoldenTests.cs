using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class SgTechnicalSelectorGoldenTests
{
    [Fact]
    public async Task G01_CommonFixed_SuggestsFermoAndKeepsCompatibleAlternatives()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "FIXED"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemFixedFermo,
            result.AppliedRuleCode);
        Assert.False(result.RequiresReview);
        Assert.Contains("SG_FIXED_ALT", result.Alternatives);
        Assert.DoesNotContain("K70", result.Alternatives);
        AssertHighConfidence(result);
    }

    [Fact]
    public async Task G02_FixedWithClientRaw3831_DoesNotChangeRanking()
    {
        var withoutRaw = await Selector().SelectAsync(
            Input(functionalType: "FIXED"),
            TestContext.Current.CancellationToken);
        var withRaw = await Selector().SelectAsync(
            Input(functionalType: "FIXED", requestedSystemRaw: "3831"),
            TestContext.Current.CancellationToken);

        AssertEquivalentSelection(withoutRaw, withRaw);
        Assert.Equal("K40", withRaw.SuggestedSystemCode);
    }

    [Fact]
    public async Task G03_ProjectingWithAssociatedFixedPanel_RemainsSingleSienaSelection()
    {
        var result = await Selector().SelectAsync(
            Input(
                functionalType: "PROJECTING",
                operation: "PROJECTING",
                features: ["ASSOCIATED_FIXED_PANEL", "LOWER_FIXED_PANEL"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemProjectingSiena,
            result.AppliedRuleCode);
        Assert.Contains("SG_PROJECTING_ALT", result.Alternatives);
        Assert.DoesNotContain("K40", result.Alternatives);
    }

    [Fact]
    public async Task G04_CommonCasement_SuggestsSiena()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "CASEMENT"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_PRIM_SIENA_CASEMENT", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemCasementSiena,
            result.AppliedRuleCode);
        Assert.Contains("SG_CASEMENT_ALT", result.Alternatives);
    }

    [Fact]
    public async Task G05_DoubleCasement_SuggestsSienaDoubleCase()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "DOUBLE_CASEMENT"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_PRIM_SIENA_DBL_CASE", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemDoubleCasementSiena,
            result.AppliedRuleCode);
        Assert.Contains("SG_DOUBLE_CASE_ALT", result.Alternatives);
    }

    [Fact]
    public async Task G06_SwingDoor_Suggests3890()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SWING_DOOR"),
            TestContext.Current.CancellationToken);

        Assert.Equal("3890", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSwingDoor3890,
            result.AppliedRuleCode);
        Assert.Contains("SG_SWING_ALT", result.Alternatives);
    }

    [Fact]
    public async Task G07_CommonSlidingDoor_SuggestsNapolesStandard()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_DOOR", operation: "SLIDING"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles,
            result.AppliedRuleCode);
        Assert.Contains("SG_SLIDING_ALT", result.Alternatives);
        Assert.DoesNotContain("K100", result.Alternatives);
    }

    [Fact]
    public async Task G08_PocketSlidingDoor_SuggestsNapolesPocketVariant()
    {
        var result = await Selector().SelectAsync(
            Input(
                functionalType: "SLIDING_DOOR",
                operation: "SLIDING",
                modulation: "XX",
                configuration: "XX PARA GUARDARSE EN UN BOLSILLO",
                features: ["POCKET"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_VEN70_POCKET_DOOR", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorPocketNapoles,
            result.AppliedRuleCode);
        Assert.Contains("SG_POCKET_ALT", result.Alternatives);
        Assert.DoesNotContain("K70", result.Alternatives);
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(9000)]
    public async Task G09_LargeSlidingDoorWidth_DoesNotForceMonacoOrExcludeNapoles(
        int widthMillimeters)
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_DOOR", width: widthMillimeters),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.DoesNotContain("K100", result.Alternatives);
    }

    [Fact]
    public async Task G10_LowSlidingWindow_SuggestsLagoWithReview()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_WINDOW", height: 1000),
            TestContext.Current.CancellationToken);

        Assert.Equal("S50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingWindowLowLago,
            result.AppliedRuleCode);
        Assert.Contains("K50", result.Alternatives);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.SlidingWindowThresholdReview,
            result.ReviewReasons);
        Assert.True(result.Confidence <= 0.70m);
    }

    [Fact]
    public async Task G11_HigherSlidingWindow_SuggestsMonzaWithReview()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_WINDOW", height: 1001),
            TestContext.Current.CancellationToken);

        Assert.Equal("K50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingWindowMonza,
            result.AppliedRuleCode);
        Assert.Contains("S50", result.Alternatives);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.SlidingWindowThresholdReview,
            result.ReviewReasons);
    }

    [Theory]
    [InlineData("L_SHAPE")]
    [InlineData("TRIANGULAR")]
    public async Task G12_G13_SpecialFixedGeometry_DoesNotChangeFamilyButRequiresReview(
        string geometryType)
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "FIXED", geometryType: geometryType),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.SpecialGeometryWithoutConstraints,
            result.ReviewReasons);
    }

    [Theory]
    [InlineData("PERGOLA", "SG_PERGOLA", SgTechnicalSelectionRuleCodes.SystemSpecialPergola)]
    [InlineData("LOUVER", "SG_LOUVER", SgTechnicalSelectionRuleCodes.SystemSpecialLouver)]
    [InlineData("SKYLIGHT", "SG_SKYLIGHT", SgTechnicalSelectionRuleCodes.SystemSpecialSkylight)]
    public async Task G14_G15_G16_SpecialSystems_GoThroughCandidateFiltering(
        string functionalType,
        string expectedSystem,
        string expectedRule)
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: functionalType),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedSystem, result.SuggestedSystemCode);
        Assert.Equal(expectedRule, result.AppliedRuleCode);
    }

    [Fact]
    public async Task G17_BathroomDivisionWithInox_SuggestsInoxCandidate()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "BATHROOM_DIVISION", features: ["INOX"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_BATH_DIV_INOX", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSpecialBathroomDivisionInox,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task G18_BathroomDivisionWithoutInox_RequiresReviewWithoutSuggestion()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "BATHROOM_DIVISION"),
            TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.BathroomDivisionMaterialUnknown,
            result.ReviewReasons);
    }

    [Theory]
    [InlineData("FIXED", "3831")]
    [InlineData("SLIDING_DOOR", "7038")]
    [InlineData("SLIDING_WINDOW", "8025")]
    [InlineData("FIXED", "MONUMENTAL")]
    public async Task R01_R04_RequestedSystemRaw_DoesNotCauseDirectSystemMapping(
        string functionalType,
        string requestedSystemRaw)
    {
        var withoutRaw = await Selector().SelectAsync(
            Input(functionalType: functionalType),
            TestContext.Current.CancellationToken);
        var withRaw = await Selector().SelectAsync(
            Input(functionalType: functionalType,
                requestedSystemRaw: requestedSystemRaw),
            TestContext.Current.CancellationToken);

        AssertEquivalentSelection(withoutRaw, withRaw);
    }

    [Fact]
    public async Task C01_RequestedEssential_DoesNotExcludeClassicSiena()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "PROJECTING", requestedCommercialLine: "ESSENTIAL"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.CommercialLineMismatch,
            result.ReviewReasons);
    }

    [Fact]
    public async Task C02_CommercialLineAddsSmallBonusButDoesNotRescueIncompatibleCandidate()
    {
        var selector = new DeterministicSgTechnicalSelector(
            new Catalog([
                System("FIXED_CLASSIC", "FIXED", "GENERIC", null, "CLASSIC"),
                System("FIXED_ESSENTIAL", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("SLIDING_ESSENTIAL", "SLIDING_DOOR", "GENERIC", null, "ESSENTIAL")
            ]));

        var result = await selector.SelectAsync(
            Input(functionalType: "FIXED", requestedCommercialLine: "ESSENTIAL"),
            TestContext.Current.CancellationToken);

        Assert.Equal("FIXED_ESSENTIAL", result.SuggestedSystemCode);
        Assert.DoesNotContain("SLIDING_ESSENTIAL", result.Alternatives);
    }

    [Fact]
    public async Task Alternatives_AreDeterministicExcludeSuggestedAndAreCappedAtThree()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL"),
                System("A_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("B_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("C_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("D_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "ESSENTIAL")
            ])).SelectAsync(
                Input(functionalType: "FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.Equal(3, result.Alternatives.Count);
        Assert.DoesNotContain("K40", result.Alternatives);
        Assert.DoesNotContain("K70", result.Alternatives);
        Assert.Equal(result.Alternatives.Order(StringComparer.Ordinal),
            result.Alternatives);
    }

    [Fact]
    public async Task PanelCountFour_DoesNotForceMonacoOrExcludeNapoles()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_DOOR", width: 8000,
                panelCount: 4),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.DoesNotContain("K100", result.Alternatives);
    }

    [Fact]
    public async Task HighPanelCountAlone_DoesNotSelectFamilyByNumber()
    {
        var result = await Selector().SelectAsync(
            Input(functionalType: "SLIDING_DOOR", panelCount: 8),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
    }

    [Fact]
    public async Task SyntheticTie_DoesNotSilentlyChooseByCode()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("A_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("B_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL")
            ])).SelectAsync(
                Input(functionalType: "FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Equal(0m, result.Confidence);
        Assert.Equal(["A_FIXED", "B_FIXED"], result.Alternatives);
        Assert.Contains(SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous,
            result.ReviewReasons);
    }

    [Fact]
    public async Task SyntheticNoCompatibleCandidate_DoesNotInventSystemCode()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL")]))
            .SelectAsync(
                Input(functionalType: "FOLDING_DOOR"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(SgTechnicalSelectionReviewReasons.TechnicalSelectionNoMatch,
            result.ReviewReasons);
    }

    [Fact]
    public async Task UnknownOnlyCatalog_ReturnsLowConfidenceReviewWithoutSuggestion()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([System("SG_UNKNOWN", null, null, null, "ESSENTIAL")]))
            .SelectAsync(
                Input(functionalType: "FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Equal(0m, result.Confidence);
        Assert.Contains(SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete,
            result.ReviewReasons);
    }

    private static void AssertHighConfidence(SgTechnicalSelectionResult result) =>
        Assert.True(result.Confidence >= 0.90m);

    private static void AssertEquivalentSelection(
        SgTechnicalSelectionResult expected,
        SgTechnicalSelectionResult actual)
    {
        Assert.Equal(expected.SuggestedSystemCode, actual.SuggestedSystemCode);
        Assert.Equal(expected.AppliedRuleCode, actual.AppliedRuleCode);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.RequiresReview, actual.RequiresReview);
        Assert.Equal(expected.ReviewReasons, actual.ReviewReasons);
        Assert.Equal(expected.Alternatives, actual.Alternatives);
    }

    private static DeterministicSgTechnicalSelector Selector() =>
        new(new Catalog(Systems()));

    private static SgTechnicalSelectionInput Input(
        string functionalType,
        string? operation = null,
        int? width = 1200,
        int? height = 1500,
        int? panelCount = null,
        string? modulation = null,
        IReadOnlyList<string>? features = null,
        string? geometryType = null,
        string? requestedCommercialLine = null,
        string? requestedSystemRaw = null,
        string? configuration = null) =>
        new(
            functionalType,
            operation,
            width,
            height,
            null,
            panelCount,
            null,
            null,
            modulation,
            null,
            features ?? [],
            geometryType,
            requestedCommercialLine,
            requestedSystemRaw,
            configuration);

    private static IReadOnlyList<ProductSystemCatalogReadModel> Systems() =>
    [
        System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL"),
        System("SG_FIXED_ALT", "FIXED", "GENERIC FIXED", null, "ESSENTIAL"),
        System("S35", "PROJECTING", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("SG_PROJECTING_ALT", "PROJECTING", "GENERIC PROJECTING", null, "ESSENTIAL"),
        System("SG_PRIM_SIENA_CASEMENT", "CASEMENT", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("SG_CASEMENT_ALT", "CASEMENT", "GENERIC CASEMENT", null, "ESSENTIAL"),
        System("SG_PRIM_SIENA_DBL_CASE", "DOUBLE_CASEMENT", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("SG_DOUBLE_CASE_ALT", "DOUBLE_CASEMENT", "GENERIC DOUBLE CASE", null, "ESSENTIAL"),
        System("3890", "SWING_DOOR", "SG 3890", null, "CLASSIC"),
        System("SG_SWING_ALT", "SWING_DOOR", "GENERIC SWING", null, "ESSENTIAL"),
        System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "ESSENTIAL"),
        System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC"),
        System("SG_VEN70_POCKET_DOOR", "SLIDING_DOOR", "VENECIA NAPOLES", "POCKET", "ESSENTIAL"),
        System("SG_POCKET_ALT", "SLIDING_DOOR", "GENERIC POCKET", "POCKET", "CLASSIC"),
        System("S50", "SLIDING_WINDOW", "PRIMAVERA LAGO", "STANDARD", "CLASSIC"),
        System("K50", "SLIDING_WINDOW", "VENECIA MONZA", "STANDARD", "ESSENTIAL"),
        System("K100", "SLIDING_WINDOW", "VENECIA MONACO", "STANDARD", "ESSENTIAL"),
        System("SG_PERGOLA", "PERGOLA", null, "STANDARD", "SPECIAL"),
        System("SG_BATH_DIV_INOX", "BATHROOM_DIVISION", null, "INOX", "SPECIAL"),
        System("SG_LOUVER", "LOUVER", null, "STANDARD", "SPECIAL"),
        System("SG_SKYLIGHT", "SKYLIGHT", null, "STANDARD", "SPECIAL"),
        System("SG_UNKNOWN", null, null, null, "ESSENTIAL")
    ];

    private static ProductSystemCatalogReadModel System(
        string code,
        string? functionalType,
        string? family,
        string? variant,
        string commercialLine) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            code,
            code,
            functionalType,
            family,
            null,
            commercialLine,
            variant,
            true,
            true,
            true,
            true,
            false,
            true);

    private sealed class Catalog(
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
        : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveSelectableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(systems.SingleOrDefault(system =>
                system.Code == code));
    }
}
