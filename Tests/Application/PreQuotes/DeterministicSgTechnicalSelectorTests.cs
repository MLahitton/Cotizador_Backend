using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
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
    public async Task GenericWindowWithFixedOperation_DerivesFixedAndFermoWins()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "FIXED"),
            TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemFixedFermo,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task GenericWindowWithSlidingOperation_DerivesSlidingWindowAndMonzaWins()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 1500),
            TestContext.Current.CancellationToken);

        Assert.Equal("K50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.VeniceWindowMonza,
            result.AppliedRuleCode);
    }

    [Fact]
    public async Task GenericWindowWithHeight2600_DoesNotResolveAsDoor()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 2600),
            TestContext.Current.CancellationToken);

        Assert.Equal("K50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.VeniceWindowMonza,
            result.AppliedRuleCode);
        Assert.DoesNotContain(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            result.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task GenericWindowWithHeight2601_ResolvesAsSlidingDoorAndPreservesOperation()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 2601),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles,
            result.AppliedRuleCode);
        Assert.Contains("K100_DOOR", result.Alternatives);
        Assert.DoesNotContain("K50", result.Alternatives);
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            result.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task SlidingWindowWithHeightOver2600_ResolvesAsSlidingDoor()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_WINDOW", height: 2800),
            TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSlidingDoorNapoles,
            result.AppliedRuleCode);
        Assert.DoesNotContain("K50", result.Alternatives);
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            result.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task GenericWindowWithNullOrInvalidHeight_DoesNotResolveAsDoor()
    {
        var nullHeight = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: null),
            TestContext.Current.CancellationToken);
        var zeroHeight = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 0),
            TestContext.Current.CancellationToken);

        Assert.NotEqual("K70", nullHeight.SuggestedSystemCode);
        Assert.NotEqual("K100_DOOR", nullHeight.SuggestedSystemCode);
        Assert.NotEqual("K70", zeroHeight.SuggestedSystemCode);
        Assert.NotEqual("K100_DOOR", zeroHeight.SuggestedSystemCode);
        Assert.DoesNotContain(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            nullHeight.ResolutionReasons ?? []);
        Assert.DoesNotContain(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            zeroHeight.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task GenericWindowWithSwingAndHeightOver2600_ResolvesAsSwingDoor()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SWING", height: 2800),
            TestContext.Current.CancellationToken);

        Assert.Equal("3890", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.SystemSwingDoor3890,
            result.AppliedRuleCode);
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            result.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task GenericWindowWithoutOperation_DoesNotInventFunctionalFamily()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: null, geometryType: "CORNER"),
            TestContext.Current.CancellationToken);

        Assert.Null(result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
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
        Assert.Contains("K100_DOOR", result.Alternatives);
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
        Assert.Equal(SgTechnicalSelectionRuleCodes.VeniceWindowMonza,
            result.AppliedRuleCode);
        Assert.Contains("S50", result.Alternatives);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task ClassicSlidingWindow_FiltersToClassicAndPrefersLago()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 2500,
                requestedCommercialLine: "CLASSIC"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S50", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.ClassicWindowSlidingLago,
            result.AppliedRuleCode);
        Assert.DoesNotContain("K50", result.Alternatives);
    }

    [Fact]
    public async Task ClassicSlidingWindowOver2600_ResolvesAsDoorAndPrefersLucca()
    {
        var result = await Selector().SelectAsync(
            Input("WINDOW", operation: "SLIDING", height: 2800,
                requestedCommercialLine: "CLASSIC"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S80", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.ClassicDoorSlidingLucca,
            result.AppliedRuleCode);
        Assert.DoesNotContain("S50", result.Alternatives);
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            result.ResolutionReasons ?? []);
    }

    [Fact]
    public async Task ClassicSlidingDoor_FiltersToClassicAndPrefersLucca()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_DOOR", requestedCommercialLine: "CLASSIC"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S80", result.SuggestedSystemCode);
        Assert.Equal(SgTechnicalSelectionRuleCodes.ClassicDoorSlidingLucca,
            result.AppliedRuleCode);
        Assert.DoesNotContain("K70", result.Alternatives);
    }

    [Fact]
    public async Task Signature_FiltersOutNonSignatureCandidates()
    {
        var result = await Selector().SelectAsync(
            Input("FIXED", requestedCommercialLine: "SIGNATURE"),
            TestContext.Current.CancellationToken);

        Assert.Equal("SIG_FIXED", result.SuggestedSystemCode);
        Assert.DoesNotContain("K40", result.Alternatives);
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
    public async Task RequestedEssential_DoesNotFilterOrAddLineMismatchReview()
    {
        var result = await Selector().SelectAsync(
            Input("PROJECTING", requestedCommercialLine: "ESSENTIAL"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.DoesNotContain(
            SgTechnicalSelectionReviewReasons.CommercialLineMismatch,
            result.ReviewReasons);
    }

    [Fact]
    public async Task RequestedBioconfort_DoesNotFilterCompatibleSystemsByLine()
    {
        var result = await Selector().SelectAsync(
            Input("PROJECTING", requestedCommercialLine: "BIOCONFORT"),
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.SuggestedSystemCode);
        Assert.DoesNotContain(
            SgTechnicalSelectionReviewReasons.CommercialLineMismatch,
            result.ReviewReasons);
    }

    [Fact]
    public async Task Monza_DoesNotWinSlidingDoor()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("MONZA_DOOR_BAD", "SLIDING_DOOR", "VENECIA MONZA", "STANDARD", "PREMIUM"),
                System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "PREMIUM")
            ])).SelectAsync(
                Input("SLIDING_DOOR"),
                TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.DoesNotContain("MONZA_DOOR_BAD", result.Alternatives);
    }

    [Fact]
    public async Task VeniceCompatibleWinsOverEquivalentLsa()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("LSA9052", "SLIDING_DOOR", "LSA 9052", "STANDARD", "PREMIUM"),
                System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "PREMIUM")
            ])).SelectAsync(
                Input("SLIDING_DOOR"),
                TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
    }

    [Fact]
    public async Task NapolesVsMonaco_KeepsReviewBecauseBoundaryIsNotDefined()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "PREMIUM"),
                System("K100_DOOR", "SLIDING_DOOR", "VENECIA MONACO", "STANDARD", "PREMIUM")
            ])).SelectAsync(
                Input("SLIDING_DOOR"),
                TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.RuleNotDefinedRequiresReview,
            result.ReviewReasons);
        Assert.Contains("K100_DOOR", result.Alternatives);
    }

    [Fact]
    public async Task TraditionalSystem_DoesNotWinAutomaticSuggested()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System("TRAD_FIXED", "FIXED", "TRADICIONAL", "STANDARD", "TRADITIONAL"),
                System("K40", "FIXED", "VENECIA FERMO", "STANDARD", "PREMIUM")
            ])).SelectAsync(
                Input("FIXED"),
                TestContext.Current.CancellationToken);

        Assert.Equal("K40", result.SuggestedSystemCode);
        Assert.DoesNotContain("TRAD_FIXED", result.Alternatives);
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
    public async Task PreSelectionHardConstraint_ExcludesOtherwisePreferredCandidate()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System(
                    "K70",
                    "SLIDING_DOOR",
                    "VENECIA NAPOLES",
                    "STANDARD",
                    "ESSENTIAL",
                    Constraint(
                        "MAX_OPENING_HEIGHT",
                        ProductSystemConstraintType.MaxHeight,
                        maxValue: 2500m,
                        severity: ProductSystemConstraintSeverity.Hard,
                        knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)),
                System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC")
            ])).SelectAsync(
                Input("SLIDING_DOOR", height: 2800),
                TestContext.Current.CancellationToken);

        Assert.Equal("SG_SLIDING_ALT", result.SuggestedSystemCode);
        Assert.DoesNotContain("K70", result.Alternatives);
    }

    [Fact]
    public async Task PreSelectionUnknownConstraint_AddsReviewWithoutExcludingCandidate()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System(
                    "K70",
                    "SLIDING_DOOR",
                    "VENECIA NAPOLES",
                    "STANDARD",
                    "ESSENTIAL",
                    Constraint(
                        "MAX_OPENING_WIDTH",
                        ProductSystemConstraintType.MaxWidth,
                        maxValue: 3000m,
                        requiresReviewWhenUnknown: true)),
                System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC")
            ])).SelectAsync(
                Input("SLIDING_DOOR", width: null),
                TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.True(result.RequiresReview);
        Assert.Contains("SYSTEM_CONSTRAINT_MAX_OPENING_WIDTH_UNKNOWN",
            result.ReviewReasons);
    }

    [Fact]
    public async Task PostDesignLeafConstraint_IsDeferredAndDoesNotPenalizePreSelection()
    {
        var result = await new DeterministicSgTechnicalSelector(
            new Catalog([
                System(
                    "K70",
                    "SLIDING_DOOR",
                    "VENECIA NAPOLES",
                    "STANDARD",
                    "ESSENTIAL",
                    Constraint(
                        "MAX_LEAF_WIDTH",
                        ProductSystemConstraintType.MaxLeafWidth,
                        scope: ProductSystemConstraintScope.Leaf,
                        evaluationStage: ConstraintEvaluationStage.PostDesign,
                        maxValue: 1200m,
                        severity: ProductSystemConstraintSeverity.Hard,
                        knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)),
                System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC")
            ])).SelectAsync(
                Input("SLIDING_DOOR", width: 5000),
                TestContext.Current.CancellationToken);

        Assert.Equal("K70", result.SuggestedSystemCode);
        Assert.False(result.RequiresReview);
    }

    [Fact]
    public async Task PanelCountAlone_DoesNotInferLeafWidthOrForceDifferentFamily()
    {
        var result = await Selector().SelectAsync(
            Input("SLIDING_DOOR", width: 8000, panelCount: 4),
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
        int? panelCount = null,
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
            panelCount,
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
        System("SIG_FIXED", "FIXED", "SIGNATURE FIXED", "STANDARD", "SIGNATURE"),
        System("3890", "SWING_DOOR", "SG 3890", null, "CLASSIC"),
        System("3890_DOOR", "DOOR", "SG 3890", null, "CLASSIC"),
        System("K70", "SLIDING_DOOR", "VENECIA NAPOLES", "STANDARD", "ESSENTIAL"),
        System("SG_SLIDING_ALT", "SLIDING_DOOR", "GENERIC SLIDING", "STANDARD", "CLASSIC"),
        System("S80", "SLIDING_DOOR", "PRIMAVERA LUCCA", "STANDARD", "CLASSIC"),
        System("SG_VEN70_POCKET_DOOR", "SLIDING_DOOR", "VENECIA NAPOLES", "POCKET", "ESSENTIAL"),
        System("SG_POCKET_ALT", "SLIDING_DOOR", "GENERIC POCKET", "POCKET", "CLASSIC"),
        System("SG_PERGOLA", "PERGOLA", null, "STANDARD", "SPECIAL"),
        System("SG_BATH_DIV_INOX", "BATHROOM_DIVISION", null, "INOX", "SPECIAL"),
        System("SG_LOUVER", "LOUVER", null, "STANDARD", "SPECIAL"),
        System("SG_SKYLIGHT", "SKYLIGHT", null, "STANDARD", "SPECIAL"),
        System("S50", "SLIDING_WINDOW", "PRIMAVERA LAGO", "STANDARD", "CLASSIC"),
        System("K50", "SLIDING_WINDOW", "VENECIA MONZA", "STANDARD", "ESSENTIAL"),
        System("K100", "SLIDING_WINDOW", "VENECIA MONACO", "STANDARD", "ESSENTIAL"),
        System("K100_DOOR", "SLIDING_DOOR", "VENECIA MONACO", "STANDARD", "ESSENTIAL"),
        System("TRAD_FIXED", "FIXED", "TRADICIONAL", "STANDARD", "TRADITIONAL"),
        System("SG_UNKNOWN", null, null, null, "ESSENTIAL")
    ];

    private static ProductSystemCatalogReadModel System(
        string code,
        string? functionalType,
        string? family,
        string? variant,
        string commercialLine,
        params ProductSystemConstraintCatalogReadModel[] constraints) =>
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
            true,
            constraints);

    private static ProductSystemConstraintCatalogReadModel Constraint(
        string code,
        ProductSystemConstraintType constraintType,
        ProductSystemConstraintScope scope = ProductSystemConstraintScope.Opening,
        ConstraintEvaluationStage evaluationStage = ConstraintEvaluationStage.PreSelection,
        ProductSystemConstraintSeverity severity = ProductSystemConstraintSeverity.Review,
        ProductSystemConstraintKnowledgeClass knowledgeClass = ProductSystemConstraintKnowledgeClass.Calibration,
        decimal? minValue = null,
        decimal? maxValue = null,
        IReadOnlyList<string>? allowedValues = null,
        bool requiresReviewWhenUnknown = false) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            code,
            constraintType,
            scope,
            evaluationStage,
            severity,
            knowledgeClass,
            minValue,
            maxValue,
            null,
            allowedValues ?? [],
            null,
            requiresReviewWhenUnknown,
            true,
            null,
            null,
            ProductSystemConstraintSourceType.SgRule,
            null,
            null);

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
