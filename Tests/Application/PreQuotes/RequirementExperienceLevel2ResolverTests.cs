using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.RequirementExperience;
using Domain.Catalogs;
using Domain.PreQuotes;
using Xunit;

#pragma warning disable xUnit1051

namespace Tests.Application.PreQuotes;

public sealed class RequirementExperienceLevel2ResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveSystem_UsesExistingSelectorPreferenceBeforeCodeTieBreak()
    {
        var systems =
            new[]
            {
                System("A_LAGO", "SLIDING_WINDOW", family: "PRIMAVERA LAGO"),
                System("Z_MONZA", "SLIDING_WINDOW", family: "VENECIA MONZA")
            };
        var resolver = Resolver(new DeterministicSgTechnicalSelector(
            new FixedProductSystemCatalogRepository(systems),
            ConstraintEvaluator()));

        var result = await resolver.ResolveSystemAsync(Input(
            functionalType: "SLIDING_WINDOW",
            height: 1500), systems, TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("Z_MONZA", result.Suggested!.Code);
    }

    [Fact]
    public async Task ResolveSystem_EquivalentCandidatesWithoutSuggestionRequireReview()
    {
        var resolver = Resolver(new FixedTechnicalSelector(null));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "FIXED"),
            [System("B_FIXED", "FIXED"), System("A_FIXED", "FIXED")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Null(result.Effective);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous, result.ReviewReasons);
    }

    [Fact]
    public async Task ResolveSystem_ReviewSuggestionDoesNotFallbackToAnotherCandidate()
    {
        var resolver = Resolver(new FixedTechnicalSelector("A_REVIEW"));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR"),
            [
                System("A_REVIEW", "SLIDING_DOOR", requiresReview: true),
                System("Z_COMPATIBLE", "SLIDING_DOOR")
            ]);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Null(result.Effective);
    }

    [Fact]
    public async Task ResolveSystem_SelectorNullDoesNotResolveCompatiblePool()
    {
        var resolver = Resolver(new FixedTechnicalSelector(null));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR"),
            [System("NAPOLES", "SLIDING_DOOR"), System("MONACO", "SLIDING_DOOR")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Null(result.Effective);
    }

    [Fact]
    public async Task ResolveSystem_IgnoresSelectorSuggestionOutsideCompatiblePool()
    {
        var resolver = Resolver(new FixedTechnicalSelector("C_FIXED"));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR"),
            [System("B_SLIDING", "SLIDING_DOOR"), System("C_FIXED", "FIXED")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Null(result.Effective);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Code == "C_FIXED");
    }

    [Theory]
    [InlineData("FIXED", "FIXED_SYSTEM")]
    [InlineData("SLIDING_WINDOW", "SLIDING_WINDOW_SYSTEM")]
    [InlineData("SLIDING_DOOR", "SLIDING_DOOR_SYSTEM")]
    [InlineData("PROJECTING", "PROJECTING_SYSTEM")]
    public async Task ResolveSystem_OnlyIncludesActiveSelectableRelatedFunctionalFamily(
        string functionalType,
        string expectedCode)
    {
        var resolver = Resolver(new FixedTechnicalSelector(expectedCode));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: functionalType),
            [
                System("FIXED_SYSTEM", "FIXED"),
                System("SLIDING_WINDOW_SYSTEM", "SLIDING_WINDOW"),
                System("SLIDING_DOOR_SYSTEM", "SLIDING_DOOR"),
                System("PROJECTING_SYSTEM", "PROJECTING"),
                System("UNKNOWN_SYSTEM", null)
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(expectedCode, candidate.Code);
        Assert.Equal(
            RequirementExperienceLevel2CandidateStates.Compatible,
            candidate.CompatibilityState);
    }

    [Fact]
    public async Task ResolveSystem_SameFunctionalFamilyWithUnknownTechnicalConstraintRequiresReview()
    {
        var resolver = Resolver(new FixedTechnicalSelector(null));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR", width: null),
            [
                System("K70", "SLIDING_DOOR", constraints:
                [
                    Constraint(
                        "MAX_OPENING_WIDTH",
                        ProductSystemConstraintType.MaxWidth,
                        maxValue: 3000m,
                        requiresReviewWhenUnknown: true)
                ]),
                System("FIXED_SYSTEM", "FIXED"),
                System("UNKNOWN_SYSTEM", null)
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("K70", candidate.Code);
        Assert.Equal(
            RequirementExperienceLevel2CandidateStates.RequiresReview,
            candidate.CompatibilityState);
        Assert.Contains("SYSTEM_CONSTRAINT_MAX_OPENING_WIDTH_UNKNOWN", candidate.ReviewReasons);
    }

    [Fact]
    public async Task ResolveSystem_RequirementCommercialLineDoesNotAffectLevel2()
    {
        var resolver = Resolver(new FixedTechnicalSelector("CLASSIC_FIXED"));
        var systems = new[]
        {
            System("CLASSIC_FIXED", "FIXED", commercialLine: "CLASSIC"),
            System("PREMIUM_FIXED", "FIXED", commercialLine: "PREMIUM")
        };

        var essential = await resolver.ResolveSystemAsync(
            Input(functionalType: "FIXED", requestedCommercialLine: "ESSENTIAL"),
            systems);
        var classic = await resolver.ResolveSystemAsync(
            Input(functionalType: "FIXED", requestedCommercialLine: "CLASSIC"),
            systems);

        Assert.Equal(essential.Suggested!.Code, classic.Suggested!.Code);
        Assert.Equal(
            essential.Candidates.Select(candidate => candidate.Code).ToArray(),
            classic.Candidates.Select(candidate => candidate.Code).ToArray());
    }

    [Fact]
    public async Task ResolveSystem_SystemTierIsNotInputForSystemLevel2()
    {
        var resolver = Resolver(new FixedTechnicalSelector("A_FIXED"));
        var systems = new[] { System("A_FIXED", "FIXED") };

        var classicTier = await resolver.ResolveSystemAsync(Input(functionalType: "FIXED"), systems, TestContext.Current.CancellationToken);
        var premiumTier = await resolver.ResolveSystemAsync(Input(functionalType: "FIXED"), systems, TestContext.Current.CancellationToken);

        Assert.Equal(classicTier.Suggested!.Code, premiumTier.Suggested!.Code);
    }

    [Fact]
    public async Task ResolveSystem_HardVerifiedConstraintFailureBlocksWhenNoViableCandidate()
    {
        var resolver = Resolver(new FixedTechnicalSelector(null));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR", width: 3200),
            [
                System("K70", "SLIDING_DOOR", constraints:
                [
                    Constraint(
                        "MAX_OPENING_WIDTH",
                        ProductSystemConstraintType.MaxWidth,
                        maxValue: 3000m,
                        severity: ProductSystemConstraintSeverity.Hard,
                        knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)
                ])
            ]);

        Assert.Equal(RequirementExperienceLevel2States.Blocked, result.State);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(RequirementExperienceLevel2CandidateStates.Incompatible, candidate.CompatibilityState);
        Assert.Contains("SYSTEM_CONSTRAINT_MAX_OPENING_WIDTH_FAIL", candidate.BlockingReasons);
    }

    [Fact]
    public async Task ResolveSystem_UnknownConstraintProducesReviewWithoutSuggestion()
    {
        var resolver = Resolver(new FixedTechnicalSelector(null));
        var result = await resolver.ResolveSystemAsync(
            Input(functionalType: "SLIDING_DOOR", width: null),
            [
                System("K70", "SLIDING_DOOR", constraints:
                [
                    Constraint(
                        "MAX_OPENING_WIDTH",
                        ProductSystemConstraintType.MaxWidth,
                        maxValue: 3000m,
                        requiresReviewWhenUnknown: true)
                ])
            ]);

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Contains("SYSTEM_CONSTRAINT_MAX_OPENING_WIDTH_UNKNOWN", result.ReviewReasons);
    }

    [Fact]
    public void ResolveGlass_TempladoOnlyAllowsMonolithicTemperedAndResolvesTemp5()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: 1000, height: 2400, panelCount: 1),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("TEMP_5", result.Suggested!.Code);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Code.StartsWith("LAM", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2600, "TEMP_6")]
    [InlineData(2800, "TEMP_8")]
    [InlineData(3001, "TEMP_10")]
    public void ResolveGlass_TempladoUsesTechnicalGeometryRules(int height, string expected)
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: 1000, height: height, panelCount: 1),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal(expected, result.Suggested!.Code);
    }

    [Fact]
    public void ResolveGlass_LaminadoAllowsRawAndTemperedLaminatedPanesAndResolvesNormalRule()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: 1000, height: 2400, panelCount: 1),
            "LAMINADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("LAM_4_4", result.Suggested!.Code);
        Assert.Contains(result.Candidates, candidate => candidate.Code == "LAMT_6_152_6_INC");
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Code.StartsWith("TEMP_", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveGlass_LaminadoJointAndHeightUsesExistingLaminatedRule()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: 2000, height: 2900, panelCount: 1),
            "LAMINADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("LAM_5_5", result.Suggested!.Code);
    }

    [Fact]
    public void ResolveGlass_TwoExplicitSegmentsUsePaneGeometry()
    {
        var resolver = Resolver();
        var item = ExtractedItem(width: 4000, height: 2400, panelCount: 2);
        AddSegment(item, 1, width: 2000, height: 2400);
        AddSegment(item, 2, width: 2000, height: 2400);

        var result = resolver.ResolveGlass(item, "TEMPLADO", GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("TEMP_10", result.Suggested!.Code);
    }

    [Fact]
    public void ResolveGlass_HeterogeneousExplicitSegmentsRequireReview()
    {
        var resolver = Resolver();
        var item = ExtractedItem(width: 3000, height: 2800, panelCount: 2);
        AddSegment(item, 1, width: 500, height: 2800);
        AddSegment(item, 2, width: 2500, height: 2800);

        var result = resolver.ResolveGlass(item, "TEMPLADO", GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Contains(GlassResolutionReasonCodes.GlassPaneHeterogeneousNeeds, result.ReviewReasons);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(
                RequirementExperienceLevel2CandidateStates.RequiresReview,
                candidate.CompatibilityState));
    }

    [Fact]
    public void ResolveGlass_ModulationWithExplicitPanePairsUsesSubmoduleGeometry()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(
                width: 4000,
                height: 2400,
                panelCount: 2,
                modulation: "Panos: 2000 x 2400 mm; 2000 x 2400 mm"),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("TEMP_10", result.Suggested!.Code);
    }

    [Fact]
    public void ResolveGlass_EvidenceWithExplicitPanePairsUsesEvidenceGeometry()
    {
        var resolver = Resolver();
        var item = ExtractedItem(width: 3200, height: 2400, panelCount: 2);
        AddEvidence(item, "Vidrio 1600 x 2400 mm y vidrio 1600 x 2400 mm.");

        var result = resolver.ResolveGlass(item, "TEMPLADO", GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("TEMP_5", result.Suggested!.Code);
        Assert.DoesNotContain(GlassResolutionReasonCodes.GlassPaneGeometryUnresolved, result.ReviewReasons);
    }

    [Fact]
    public void ResolveGlass_PanelCountWithoutExplicitPaneDimensionsRequiresReview()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: 4000, height: 2400, panelCount: 2),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.Contains(GlassResolutionReasonCodes.GlassPaneGeometryUnresolved, result.ReviewReasons);
    }

    [Fact]
    public void ResolveGlass_ExplicitGlassSignalWinsWhenGeometryIsInsufficient()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: null, height: null, panelCount: null, glassTypeNormalized: "templado 8 mm", glassThicknessMm: 8m),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Resolved, result.State);
        Assert.Equal("TEMP_8", result.Suggested!.Code);
    }

    [Fact]
    public void ResolveGlass_InsufficientGeometryWithoutExactSignalRequiresReview()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            ExtractedItem(width: null, height: null, panelCount: null),
            "TEMPLADO",
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.RequiresReview, result.State);
        Assert.Null(result.Suggested);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(RequirementExperienceLevel2CandidateStates.RequiresReview, candidate.CompatibilityState));
    }

    [Fact]
    public void ResolveGlass_WithoutLevel1FamilyIsPending()
    {
        var resolver = Resolver();
        var result = resolver.ResolveGlass(
            item: null,
            glassFamily: null,
            GlassCatalog());

        Assert.Equal(RequirementExperienceLevel2States.Pending, result.State);
        Assert.Null(result.Suggested);
        Assert.Empty(result.Candidates);
        Assert.Contains("GLASS_FAMILY_NOT_RESOLVED", result.ReviewReasons);
    }

    [Theory]
    [InlineData("POCKET", "SLIDING_DOOR", false)]
    [InlineData(null, "SLIDING_DOOR", true)]
    [InlineData(null, "FIXED", false)]
    public async Task Resolve_ClassicSlidingDoor_RespectsRealSelectorDecision(
        string? feature, string candidateType, bool expectedSuggestion)
    {
        var extracted = ExtractedItem(1200, 2200, 2,
            elementType: StructuredElementType.Door,
            functionalType: "SLIDING_DOOR", operation: "SLIDING",
            specialFeatures: feature is null ? [] : [feature]);
        var proposal = RequirementTechnicalProposal.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, Now);
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id, extracted.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            extracted.Sequence, extracted.Reference, extracted.Description, extracted.ElementType,
            extracted.Quantity, extracted.WidthMillimeters, extracted.HeightMillimeters,
            0.9m, 0.9m, 0.9m, 0.9m, false, true, true,
            [], [], [], [], 0, null, null, "NotEvaluated", Now);
        typeof(RequirementTechnicalProposalItem).GetProperty("ExtractedItem")!.SetValue(item, extracted);
        proposal.AddItem(item);
        var system = System("CLASSIC_STANDARD", candidateType,
            commercialLine: "CLASSIC", family: "VENECIA NAPOLES") with
        {
            Name = "PUERTA CORREDIZA LINEA CLASSIC",
            TechnicalName = "PUERTA CORREDIZA LINEA CLASSIC"
        };
        ProductSystemCatalogReadModel[] systems = [system];
        var selector = new DeterministicSgTechnicalSelector(
            new FixedProductSystemCatalogRepository(systems), ConstraintEvaluator());
        var input = RequirementFunctionalSelectionContextResolver.ResolveInput(item, requestedCommercialLine: null);
        var selection = await selector.SelectAsync(input, TestContext.Current.CancellationToken);
        var result = await Resolver(selector).ResolveAsync(proposal, item,
            new("CLASSIC", "TEMPLADO", true, 5, 5), null, systems, GlassCatalog(),
            TestContext.Current.CancellationToken);

        if (expectedSuggestion)
        {
            Assert.Equal(system.Code, selection.SuggestedSystemCode);
            Assert.Equal(system.Code, result.System.Suggested!.Code);
            Assert.Equal(RequirementExperienceLevel2States.Resolved, result.System.State);
        }
        else
        {
            Assert.Null(selection.SuggestedSystemCode);
            Assert.Null(result.System.Suggested);
            Assert.Null(result.System.Effective);
            Assert.NotEqual(RequirementExperienceLevel2States.Resolved, result.System.State);
            if (feature == "POCKET")
            {
                Assert.Equal(RequirementExperienceLevel2States.Blocked, result.System.State);
                Assert.Equal("INCOMPATIBLE", Assert.Single(result.System.Candidates).CompatibilityState);
                Assert.Contains("SPECIAL_FEATURE_POCKET", result.System.BlockingReasons);
            }
            else
            {
                Assert.Equal(RequirementExperienceLevel2States.Blocked, result.System.State);
                Assert.Empty(result.System.Candidates);
            }
        }
    }

    [Theory]
    [InlineData("SYS_CUERPO_FIJO_TRADICIONAL_SG", "FIXED", "FIXED", "CLASSIC", false, true)]
    [InlineData("SYS_CUERPO_PROYECTANTE_TRADICI", "PROJECTING", "PROJECTING", "CLASSIC", false, true)]
    [InlineData("SYS_PUERTA_CORREDIZA_TRADICI_2", "SLIDING_DOOR", "SLIDING_DOOR", "CLASSIC", false, true)]
    [InlineData("SYS_CUERPO_FIJO_TRADICIONAL_SG", "FIXED", "FIXED", "CLASSIC", true, false)]
    [InlineData("SYS_CUERPO_FIJO_TRADICIONAL_SG", "FIXED", "SLIDING_DOOR", "CLASSIC", false, false)]
    [InlineData("SYS_CUERPO_FIJO_TRADICIONAL_SG", "FIXED", "FIXED", "PREMIUM", false, false)]
    public async Task Resolve_TraditionalEligibilityIsExperienceOnly(
        string code, string candidateType, string itemType, string tier,
        bool hardFailure, bool expectedSuggestion)
    {
        var extracted = ExtractedItem(1200, 2200, 1,
            functionalType: itemType, operation: null);
        var proposal = RequirementTechnicalProposal.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, Now);
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id, extracted.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            extracted.Sequence, extracted.Reference, extracted.Description, extracted.ElementType,
            extracted.Quantity, extracted.WidthMillimeters, extracted.HeightMillimeters,
            0.9m, 0.9m, 0.9m, 0.9m, false, true, true,
            [], [], [], [], 0, null, null, "NotEvaluated", Now);
        typeof(RequirementTechnicalProposalItem).GetProperty("ExtractedItem")!.SetValue(item, extracted);
        proposal.AddItem(item);
        var system = System(code, candidateType, commercialLine: "TRADITIONAL",
            constraints: hardFailure ? [Constraint("MAX_WIDTH", ProductSystemConstraintType.MaxWidth,
                maxValue: 1000m, severity: ProductSystemConstraintSeverity.Hard,
                knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)] : []) with
        {
            Name = candidateType == "FIXED" ? "CUERPO FIJO TRADICIONAL SG" : "SISTEMA TRADICIONAL",
            TechnicalName = "SISTEMA TRADICIONAL"
        };
        ProductSystemCatalogReadModel[] systems = [system];
        var selector = new DeterministicSgTechnicalSelector(
            new FixedProductSystemCatalogRepository(systems), ConstraintEvaluator());
        var input = RequirementFunctionalSelectionContextResolver.ResolveInput(item, requestedCommercialLine: null);

        Assert.True(input.EnforceLegacyCommercialLineEligibility);
        var legacy = await selector.SelectAsync(input, TestContext.Current.CancellationToken);
        Assert.Null(legacy.SuggestedSystemCode);
        Assert.Equal("CLASSIC", ProductSystemExperienceTierResolver.Resolve(system));

        var result = await Resolver(selector).ResolveAsync(proposal, item,
            new(tier, "TEMPLADO", true, 5, 5), null, systems, GlassCatalog(),
            TestContext.Current.CancellationToken);
        if (expectedSuggestion)
        {
            Assert.Equal(code, result.System.Suggested!.Code);
            Assert.Equal(RequirementExperienceLevel2States.Resolved, result.System.State);
        }
        else
        {
            Assert.Null(result.System.Suggested);
            Assert.Null(result.System.Effective);
            Assert.Equal(RequirementExperienceLevel2States.Blocked, result.System.State);
            if (hardFailure)
                Assert.Contains("SYSTEM_CONSTRAINT_MAX_WIDTH_FAIL", result.System.BlockingReasons);
            else
                Assert.Empty(result.System.Candidates);
        }
        Assert.Equal("TRADITIONAL", system.CommercialLine);
    }

    [Theory]
    [InlineData("SLIDING_DOOR", "GENERIC", "STANDARD", "POCKET", "SPECIAL_FEATURE_POCKET")]
    [InlineData("SLIDING_DOOR", "VENECIA MONZA", "STANDARD", null, "VENICE_MONZA_WINDOW_ONLY")]
    [InlineData("SWING_DOOR", "GENERIC", "DOUBLE", null, "SWING_DOOR_SPECIAL_VARIANT_EVIDENCE")]
    [InlineData("SWING_DOOR", "GENERIC", "INOX", null, "SWING_DOOR_SPECIAL_VARIANT_EVIDENCE")]
    [InlineData("SWING_DOOR", "GENERIC", "SPECIAL", null, "SWING_DOOR_SPECIAL_VARIANT_EVIDENCE")]
    public async Task EligibilityAndExperienceShareTechnicalExclusions(
        string type, string family, string variant, string? feature, string reason)
    {
        var system = System("CANDIDATE", type, family: family) with { Variant = variant };
        var input = Input(type) with { SpecialFeatures = feature is null ? [] : [feature] };
        var selector = new DeterministicSgTechnicalSelector(
            new FixedProductSystemCatalogRepository([system]), ConstraintEvaluator());
        var eligibility = selector.EvaluateCandidateEligibility(system, input);
        Assert.True(eligibility.IsIncompatible);
        Assert.Contains(reason, eligibility.BlockingReasons);
        var automatic = await selector.SelectAsync(input, TestContext.Current.CancellationToken);
        Assert.Null(automatic.SuggestedSystemCode);
        var manual = await Resolver(selector).ResolveSystemAsync(input, [system]);
        Assert.Equal("INCOMPATIBLE", Assert.Single(manual.Candidates).CompatibilityState);
        Assert.Contains(reason, manual.BlockingReasons);
    }

    private static RequirementExperienceLevel2Resolver Resolver(
        ISgTechnicalSelector? selector = null) =>
        new(
            ConstraintEvaluator(),
            new GlassCandidateResolver(),
            selector ?? new FixedTechnicalSelector(null));

    private static SgProductSystemConstraintEvaluator ConstraintEvaluator() =>
        new(new FixedTimeProvider(Now));

    private static SgTechnicalSelectionInput Input(
        string? functionalType,
        int? width = 1200,
        int? height = 2200,
        string? requestedCommercialLine = null) =>
        new(
            functionalType,
            null,
            width,
            height,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            requestedCommercialLine,
            null);

    private static ProductSystemCatalogReadModel System(
        string code,
        string? functionalType,
        string commercialLine = "ESSENTIAL",
        string family = "FAMILY",
        bool requiresReview = false,
        IReadOnlyList<ProductSystemConstraintCatalogReadModel>? constraints = null) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            code,
            code,
            functionalType,
            family,
            "SERIES",
            commercialLine,
            "STANDARD",
            true,
            true,
            true,
            false,
            requiresReview,
            true,
            constraints ?? []);

    private static ProductSystemConstraintCatalogReadModel Constraint(
        string code,
        ProductSystemConstraintType constraintType,
        decimal? minValue = null,
        decimal? maxValue = null,
        ProductSystemConstraintSeverity severity = ProductSystemConstraintSeverity.Review,
        ProductSystemConstraintKnowledgeClass knowledgeClass = ProductSystemConstraintKnowledgeClass.Calibration,
        bool requiresReviewWhenUnknown = false) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            code,
            constraintType,
            ProductSystemConstraintScope.Opening,
            ConstraintEvaluationStage.PreSelection,
            severity,
            knowledgeClass,
            minValue,
            maxValue,
            null,
            [],
            null,
            requiresReviewWhenUnknown,
            true,
            null,
            null,
            ProductSystemConstraintSourceType.SgRule,
            null,
            null);

    private static IReadOnlyList<GlassTypeCatalogReadModel> GlassCatalog() =>
    [
        Glass("TEMP_5", "MONOLITHIC", "TEMPERED", 5m),
        Glass("TEMP_6", "MONOLITHIC", "TEMPERED", 6m),
        Glass("TEMP_8", "MONOLITHIC", "TEMPERED", 8m),
        Glass("TEMP_10", "MONOLITHIC", "TEMPERED", 10m),
        Glass("RAW_6", "MONOLITHIC", "RAW", 6m),
        Glass("LAM_4_4", "LAMINATED", "RAW", 4m, 4m),
        Glass("LAM_5_5", "LAMINATED", "RAW", 5m, 5m),
        Glass("LAMT_6_152_6_INC", "LAMINATED", "TEMPERED", 6m, 6m)
    ];

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string family,
        string composition,
        decimal? outerThicknessMm = null,
        decimal? innerThicknessMm = null,
        bool requiresReview = false) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            null,
            true,
            null,
            family,
            composition,
            null,
            outerThicknessMm,
            innerThicknessMm,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            requiresReview);

    private static RequirementExtractedItem ExtractedItem(
        int? width,
        int? height,
        int? panelCount,
        StructuredElementType elementType = StructuredElementType.Window,
        string? functionalType = "WINDOW",
        string? operation = "FIXED",
        string? glassTypeNormalized = null,
        decimal? glassThicknessMm = null,
        string? arrangement = null,
        string? modulation = null,
        IReadOnlyList<string>? specialFeatures = null) =>
        RequirementExtractedItem.Create(
            requirementExtractionResultId: Guid.NewGuid(),
            ai2ElementId: "item-1",
            sequence: 1,
            reference: "V-01",
            description: "V-01",
            elementType: elementType,
            quantity: 1,
            widthMillimeters: width,
            heightMillimeters: height,
            areaSquareMeters: width is not null && height is not null
                ? width.Value * height.Value / 1_000_000m
                : null,
            confidence: 0.9m,
            extractionStatus: RequirementExtractionValueStatus.Explicit,
            requiresReview: false,
            reviewReasons: [],
            functionalType: functionalType,
            operation: operation,
            panelCount: panelCount,
            movablePanelCount: null,
            fixedPanelCount: null,
            arrangement: arrangement,
            modulation: modulation,
            openingDirection: null,
            specialFeatures: specialFeatures ?? [],
            geometryType: null,
            requestedSystemRaw: null,
            requestedProfileRaw: null,
            glassRawSpecification: glassTypeNormalized,
            glassTypeRaw: glassTypeNormalized,
            glassTypeNormalized: glassTypeNormalized,
            glassThicknessMm: glassThicknessMm,
            glassColorRaw: null,
            glassColorNormalized: null,
            glassTreatmentRaw: null,
            glassTreatmentNormalized: null,
            glassComposition: null,
            glassCoating: null,
            glassTransparency: null,
            glassRequiresReview: false,
            finishRawDescription: null,
            finishNormalizedType: null,
            finishColorRaw: null,
            finishColorNormalized: null,
            finishTextureRaw: null,
            finishTextureNormalized: null,
            finishExplicitCode: null,
            finishRequiresReview: false,
            createdAtUtc: Now);

    private static void AddSegment(
        RequirementExtractedItem item,
        int sequence,
        int width,
        int height) =>
        item.AddSegment(RequirementExtractedItemSegment.Create(
            item.Id,
            sequence,
            "pane",
            width,
            height,
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            Now));

    private static void AddEvidence(
        RequirementExtractedItem item,
        string text) =>
        item.AddEvidence(RequirementExtractedItemEvidence.Create(
            item.Id,
            pageNumber: 1,
            sourceType: EvidenceSourceType.Native,
            text,
            sheetName: null,
            cellRange: null,
            sourceId: null,
            confidence: 0.9m,
            status: RequirementExtractionValueStatus.Explicit,
            Now));

    private sealed class FixedTechnicalSelector(string? suggestedCode) : ISgTechnicalSelector
    {
        public Task<SgTechnicalSelectionResult> SelectAsync(
            SgTechnicalSelectionInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SgTechnicalSelectionResult(
                suggestedCode,
                SgTechnicalSelectionRuleCodes.SystemCandidateRanking,
                suggestedCode is null ? 0m : 1m,
                suggestedCode is null,
                suggestedCode is null ? [SgTechnicalSelectionReviewReasons.TechnicalSelectionAmbiguous] : [],
                []));
    }

    private sealed class FixedProductSystemCatalogRepository(
        IReadOnlyList<ProductSystemCatalogReadModel> systems) : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveSelectableAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(systems.FirstOrDefault(system =>
                system.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
