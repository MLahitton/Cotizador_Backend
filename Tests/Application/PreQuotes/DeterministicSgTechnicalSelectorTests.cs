using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class DeterministicSgTechnicalSelectorTests
{
    [Fact]
    public async Task Fixed_CommonElement_FermoWinsAndOtherFixedSurvivesAsAlternative()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemFixedFermo,
            result.AppliedRuleCode);
        Assert.Contains("SG_FIXED_ALT", result.Alternatives);
        Assert.DoesNotContain("K70", result.Alternatives);
    }

    [Fact]
    public async Task FunctionalType_IncompatibleCandidate_IsExcluded()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("K70", result.Alternatives);
        Assert.NotEqual("K70", result.SuggestedSystemCode);
    }

    [Fact]
    public async Task UnknownMetadata_DoesNotBeatStrongCompatibleCandidate()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.Contains("SG_UNKNOWN", result.Alternatives);
    }

    [Fact]
    public async Task Projecting_SienaWinsAndOtherProjectingSurvives()
    {
        var result = await Selector().SelectAsync(
            Input("PROJECTING"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemProjectingSiena,
            result.AppliedRuleCode);
        Assert.Contains("SG_PROJECTING_ALT", result.Alternatives);
    }

    [Fact]
    public async Task Casement_SienaPriorWins()
    {
        var result = await Selector().SelectAsync(
            Input("CASEMENT"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_PRIM_SIENA_CASEMENT", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemCasementSiena,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task DoubleCasement_SienaPriorWins()
    {
        var result = await Selector().SelectAsync(
            Input("DOUBLE_CASEMENT"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_PRIM_SIENA_DBL_CASE", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemDoubleCasementSiena,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task SwingDoor_3890PriorWins()
    {
        var result = await Selector().SelectAsync(
            Input("SWING_DOOR"),
            TestContext.Current.CancellationToken);

        Assert.Equal("3890", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSwingDoor3890,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task DoorWithSwingOperation_3890PriorWins()
    {
        var result = await Selector().SelectAsync(
            Input("DOOR", operation: "SWING"),
            TestContext.Current.CancellationToken);

        Assert.Equal("3890_DOOR", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSwingDoor3890,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task SlidingDoor_NapolesWinsAndOtherSlidingDoorSurvives()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_DOOR"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles,
            result.AppliedRuleCode);
        Assert.Contains("SG_SLIDING_ALT", result.Alternatives);
    }

    [Fact]
    public async Task SlidingDoorPocket_StandardDoesNotWinAndPocketAlternativeSurvives()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_DOOR", feature: "POCKET"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_VEN70_POCKET_DOOR", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorPocketNapoles,
            result.AppliedRuleCode);
        Assert.Contains("SG_POCKET_ALT", result.Alternatives);
        Assert.DoesNotContain("K70", result.Alternatives);
    }

    [Theory]
    [InlineData("PERGOLA", "SG_PERGOLA", SgTechnicalSelectionRuleCodes.SystemSpecialPergola)]
    [InlineData("LOUVER", "SG_LOUVER", SgTechnicalSelectionRuleCodes.SystemSpecialLouver)]
    [InlineData("SKYLIGHT", "SG_SKYLIGHT", SgTechnicalSelectionRuleCodes.SystemSpecialSkylight)]
    public async Task SpecialSystems_GoThroughCandidateRanking(
        string functionalType,
        string expectedCode,
        string expectedRule)
    {
        var result = await Selector().SelectAsync(
            Input(functionalType),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCode, result.SuggestedSystemCode);
        Assert.Equal(expectedRule, result.AppliedRuleCode);
    }

    [Fact]
    public async Task BathroomDivision_WithInox_SelectsInoxCandidate()
    {
        var result = await Selector().SelectAsync(
            Input("BATHROOM_DIVISION", feature: "INOX"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_BATH_DIV_INOX", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSpecialBathroomDivisionInox,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task BathroomDivision_WithoutInox_RequiresReviewWithoutSuggestion()
    {
        var result = await Selector().SelectAsync(
            Input("BATHROOM_DIVISION"),
            TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.BathroomDivisionMaterialUnknown,
            result.ReviewReasons);
    }

    [Fact]
    public async Task SlidingWindowLow_LagoWinsMonzaAlternativeAndRequiresReview()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_WINDOW", height: 900),
            TestContext.Current.CancellationToken);

        Assert.Equal("S50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingWindowLowLago,
            result.AppliedRuleCode);
        Assert.Contains("K50", result.Alternatives);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.SlidingWindowThresholdReview,
            result.ReviewReasons);
    }

    [Fact]
    public async Task SlidingWindowHigher_MonzaWinsLagoCanRemainAlternativeAndRequiresReview()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_WINDOW", height: 1500),
            TestContext.Current.CancellationToken);

        Assert.Equal("K50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingWindowMonza,
            result.AppliedRuleCode);
        Assert.Contains("S50", result.Alternatives);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task SameCommercialLine_AddsSoftBonus()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED", requestedCommercialLine: "ESSENTIAL"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.False(result.RequiresReview);
    }

    [Fact]
    public async Task RequestedEssential_DoesNotExcludeTechnicallyPreferredSiena()
    {
        var result = await Selector().SelectAsync(
            Input("PROJECTING", requestedCommercialLine: "ESSENTIAL"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.CommercialLineMismatch,
            result.ReviewReasons);
    }

    [Theory]
    [InlineData("FIXED", "3831", "K40")]
    [InlineData("SLIDING_DOOR", "7038", "K70")]
    [InlineData("SLIDING_WINDOW", "8025", "K50")]
    public async Task RequestedSystemRaw_DoesNotDriveDirectMapping(
        string functionalType,
        string requested,
        string expected)
    {
        var result = await Selector().SelectAsync(
            Input(functionalType, requestedSystemRaw: requested),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.SuggestedSystemCode);
    }

    [Fact]
    public async Task LargeWidth_DoesNotForceMonaco()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_DOOR", width: 8000),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.NotEqual("K100", result.SuggestedSystemCode);
    }

    [Fact]
    public async Task ExactTie_DoesNotSilentlyChooseByCode()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("A_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL"),
                System("B_FIXED", "FIXED", "GENERIC", null, "ESSENTIAL")
            ])).SelectAsync(
                Input("FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous,
            result.ReviewReasons);
        Assert.Equal(["A_FIXED", "B_FIXED"], result.Alternatives);
    }

    [Fact]
    public async Task NoCompatibleCandidates_ReturnsReviewWithoutSuggestion()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "ESSENTIAL")]))
            .SelectAsync(
                Input("FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.TechnicalSelectionNoMatch,
            result.ReviewReasons);
    }

    [Fact]
    public async Task OnlyUnknownCandidates_RequiresReviewWithoutSuggestion()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([System("SG_UNKNOWN", null, null, null, "ESSENTIAL")]))
            .SelectAsync(
                Input("FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMetadataIncomplete,
            result.ReviewReasons);
        Assert.Equal(["SG_UNKNOWN"], result.Alternatives);
    }

    [Fact]
    public async Task SpecialGeometry_DoesNotChangeFamilyButRequiresReview()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED", geometryType: "ARCH"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.SpecialGeometryWithoutConstraints,
            result.ReviewReasons);
    }

    [Fact]
    public async Task CatalogEmpty_DoesNotInventCode()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([])).SelectAsync(
                Input("FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.TechnicalSelectionCatalogMatchNotFound,
            result.ReviewReasons);
    }

    [Fact]
    public async Task SameInput_IsDeterministic()
    {
        var selector = Selector();
        var input = Input("SLIDING_DOOR", feature: "POCKET");

        var first = await selector.SelectAsync(
            input,
            TestContext.Current.CancellationToken);
        var second = await selector.SelectAsync(
            input,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.SuggestedSystemCode, second.SuggestedSystemCode);
        Assert.Equal(first.AppliedRuleCode, second.AppliedRuleCode);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.RequiresReview, second.RequiresReview);
        Assert.Equal(first.ReviewReasons, second.ReviewReasons);
        Assert.Equal(first.Alternatives, second.Alternatives);
    }

    private static DeterministicSgTechnicalSelector Selector() =>
        new(new Catalog(Systems()));

    private static SgTechnicalSelectionInput Input(
        string functionalType,
        string? operation = null,
        int? height = 1500,
        int? width = 1200,
        string? feature = null,
        string? requestedCommercialLine = null,
        string? requestedSystemRaw = null,
        string? geometryType = null) =>
        new(
            functionalType,
            operation,
            width,
            height,
            null,
            null,
            null,
            null,
            null,
            null,
            feature is null ? [] : [feature],
            geometryType,
            requestedCommercialLine,
            requestedSystemRaw);

    private static IReadOnlyList<ProductSystemCatalogReadModel> Systems() =>
    [
        System("K40", "FIXED", "VENECIA FERMO", null, "ESSENTIAL"),
        System("SG_FIXED_ALT", "FIXED", "GENERIC FIXED", null, "ESSENTIAL"),
        System("S35", "PROJECTING", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("SG_PROJECTING_ALT", "PROJECTING", "GENERIC PROJECTING", null, "ESSENTIAL"),
        System("SG_PRIM_SIENA_CASEMENT", "CASEMENT", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("SG_PRIM_SIENA_DBL_CASE", "DOUBLE_CASEMENT", "PRIMAVERA SIENA", null, "CLASSIC"),
        System("3890", "SWING_DOOR", "SG 3890", null, "CLASSIC"),
        System("3890_DOOR", "DOOR", "SG 3890", null, "CLASSIC"),
        System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "ESSENTIAL"),
        System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC"),
        System("SG_VEN70_POCKET_DOOR", "SLIDING_DOOR", "VENECIA NAPOLES", "POCKET", "ESSENTIAL"),
        System("SG_POCKET_ALT", "SLIDING_DOOR", "GENERIC POCKET", "POCKET", "CLASSIC"),
        System("SG_PERGOLA", "PERGOLA", null, "STANDARD", "SPECIAL"),
        System("SG_BATH_DIV_INOX", "BATHROOM_DIVISION", null, "INOX", "SPECIAL"),
        System("SG_LOUVER", "LOUVER", null, "STANDARD", "SPECIAL"),
        System("SG_SKYLIGHT", "SKYLIGHT", null, "STANDARD", "SPECIAL"),
        System("S50", "SLIDING_WINDOW", "PRIMAVERA LAGO", "STANDARD", "CLASSIC"),
        System("K50", "SLIDING_WINDOW", "VENECIA MONZA", "STANDARD", "ESSENTIAL"),
        System("K100", "SLIDING_WINDOW", "VENECIA MONACO", "STANDARD", "ESSENTIAL"),
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