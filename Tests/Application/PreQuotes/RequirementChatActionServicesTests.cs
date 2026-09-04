using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
using Application.PreQuotes.RequirementChatActions;
using Application.PreQuotes.TechnicalProposalReadiness;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RequirementChatActionServicesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RequirementId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ProposalId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid ItemAId = Guid.Parse("10000000-0000-0000-0000-00000000000a");
    private static readonly Guid ItemBId = Guid.Parse("10000000-0000-0000-0000-00000000000b");
    private static readonly Guid SystemAId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SystemBId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SystemCId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemDId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid GlassAId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid GlassBId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid GlassCId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid GlassDId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid GlassEId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    private static readonly Guid FinishAId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid FinishBId = Guid.Parse("40000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task RequirementChatAction_PlanItemChangeSystem_DoesNotWriteOrRepriceAndRequiresConfirmation()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command("CHANGE_SYSTEM", contextItemId: ItemAId, requestedValue: "K72"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        Assert.True(result.Plan.RequiresConfirmation);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Equal(ItemAId, action.TargetTechnicalProposalItemId);
        Assert.Equal(SystemBId, action.ResolvedCatalogEntity!.Id);
        Assert.Null(context.Reader.Item(ItemAId).Selected);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(0, context.Pricing.RepriceCount);
    }

    [Fact]
    public async Task RequirementChatAction_ConfirmWithPricing_ExecutesOnceAndRepricesItem()
    {
        var context = CreateContext(hasPricing: true);
        var plan = await ReadyPlanAsync(context, "CHANGE_SYSTEM", ItemAId, "K72");

        var first = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);
        var second = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", first.Plan!.Status);
        Assert.Equal("EXECUTED", second.Plan!.Status);
        Assert.Equal("PRICING_UPDATED", first.Plan.PricingStatus);
        Assert.Equal(1, context.Pricing.RepriceCount);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(SystemBId, context.Reader.Item(ItemAId).Selected!.System!.Id);
    }

    [Fact]
    public async Task RequirementChatAction_ItemScope_ContextItemWinsOverConflictingTargetReference()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_SYSTEM",
                contextItemId: ItemAId,
                targetReference: "V-02",
                requestedValue: "K72"),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal(ItemAId, action.TargetTechnicalProposalItemId);
        Assert.Equal("V-01", action.TargetReference);
    }

    [Fact]
    public async Task RequirementChatAction_RequirementScope_UniqueReferenceResolvesTarget()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command("CHANGE_FINISH", scope: "REQUIREMENT", targetReference: "V-02", requestedValue: "WHITE_MATTE"),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal(ItemBId, action.TargetTechnicalProposalItemId);
        Assert.Equal("V-02", action.TargetReference);
    }

    [Fact]
    public async Task RequirementChatAction_DuplicateReferenceNeedsClarificationAndDoesNotWrite()
    {
        var context = CreateContext(duplicateReference: true);

        var result = await context.Plan.ExecuteAsync(
            Command("CHANGE_SYSTEM", scope: "REQUIREMENT", targetReference: "V-01", requestedValue: "K72"),
            TestContext.Current.CancellationToken);

        Assert.Equal("NEEDS_CLARIFICATION", result.Plan!.Status);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Contains("TARGET_REFERENCE_AMBIGUOUS", action.ValidationReasons);
        Assert.Equal(2, action.AvailableOptions.Count);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(0, context.Pricing.RepriceCount);
    }

    [Fact]
    public async Task RequirementChatAction_InvalidCatalogValueDoesNotWriteOrReprice()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command("CHANGE_GLASS", contextItemId: ItemAId, requestedValue: "NO_EXISTE"),
            TestContext.Current.CancellationToken);

        Assert.Equal("NEEDS_CLARIFICATION", result.Plan!.Status);
        var action = Assert.Single(result.Plan.Actions);
        Assert.Contains("GLASS_NOT_FOUND", action.ValidationReasons);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(0, context.Pricing.RepriceCount);
    }

    [Fact]
    public async Task RequirementChatAction_GlassAttributesResolveTemperedThicknessWithoutExactDisplayName()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_GLASS",
                contextItemId: ItemAId,
                requestedValue: "vidrio templado de 6 mm",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Glass: new RequirementChatRequestedGlassAttributes(
                        Composition: "TEMPERED",
                        OuterThicknessMm: 6m))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal(GlassAId, action.ResolvedCatalogEntity!.Id);
        Assert.Equal("TEMP_6", action.ResolvedCatalogEntity.Code);
    }

    [Fact]
    public async Task RequirementChatAction_GlassAlternativesPreferSameCompositionWhenThicknessIsUnavailable()
    {
        var context = CreateContext(
            glassValues:
            [
                Glass("TEMP_5", GlassCId, true),
                Glass("TEMP_8", GlassBId, true),
                Glass("TEMP_10", GlassDId, true),
                Glass("LAM_4_4", GlassEId, true)
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_GLASS",
                contextItemId: ItemAId,
                requestedValue: "vidrio templado de 6 mm",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Glass: new RequirementChatRequestedGlassAttributes(
                        Composition: "TEMPERED",
                        OuterThicknessMm: 6m))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("NEEDS_CLARIFICATION", result.Plan.Status);
        Assert.Contains("GLASS_NOT_FOUND", action.ValidationReasons);
        Assert.Equal(["TEMP_5", "TEMP_8", "TEMP_10"], action.AvailableOptions.Take(3).Select(option => option.Code).ToArray());
        Assert.DoesNotContain(action.AvailableOptions, option => option.Code == "LAM_4_4");
    }

    [Fact]
    public async Task RequirementChatAction_GlassAttributesKeepLaminatedCandidatesInLaminatedSpace()
    {
        var context = CreateContext(
            glassValues:
            [
                Glass("TEMP_6", GlassAId, true),
                GlassCatalog("LAM_4_4", GlassBId, "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC", "LAMINATED", null, "RAW", 4m, 4m, null),
                GlassCatalog("LAM_5_5", GlassCId, "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC", "LAMINATED", null, "RAW", 5m, 5m, null)
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_GLASS",
                contextItemId: ItemAId,
                requestedValue: "laminado 4+4",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Glass: new RequirementChatRequestedGlassAttributes(
                        Composition: "LAMINATED",
                        OuterThicknessMm: 4m,
                        InnerThicknessMm: 4m))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal("LAM_4_4", action.ResolvedCatalogEntity!.Code);
    }

    [Fact]
    public async Task RequirementChatAction_GlassAttributesKeepChamberCandidatesFirst()
    {
        var context = CreateContext(
            glassValues:
            [
                Glass("TEMP_6", GlassAId, true),
                GlassCatalog("DVH_5_12_6", GlassDId, "COMPOSICION TEMPLADO 5 MM INC + CAMARA 12 MM + TEMPLADO 6 MM INC", "IGU", null, "TEMPERED", 5m, 6m, 12m)
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_GLASS",
                contextItemId: ItemAId,
                requestedValue: "doble vidrio con camara 12",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Glass: new RequirementChatRequestedGlassAttributes(
                        Family: "IGU",
                        ChamberThicknessMm: 12m))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal("DVH_5_12_6", action.ResolvedCatalogEntity!.Code);
    }

    [Fact]
    public async Task RequirementChatAction_SystemAlternativesPreserveCurrentFunctionalTypeWhenUserDoesNotRequestTypeChange()
    {
        var context = CreateContext(
            systemValues:
            [
                System("SD_MONACO", SystemBId, "SLIDING_DOOR", "SLIDING", "VENECIA MONACO"),
                System("SD_FERMO", SystemCId, "SLIDING_DOOR", "SLIDING", "VENECIA FERMO"),
                System("SW_MONZA", SystemDId, "SLIDING_WINDOW", "SLIDING", "MONZA")
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_SYSTEM",
                contextItemId: ItemAId,
                requestedValue: "Venecia",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    System: new RequirementChatRequestedSystemAttributes(
                        CommercialName: "VENECIA"))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("NEEDS_CLARIFICATION", result.Plan.Status);
        Assert.Equal(["SD_FERMO", "SD_MONACO"], action.AvailableOptions.Select(option => option.Code).ToArray());
    }

    [Fact]
    public async Task RequirementChatAction_SystemAttributesCanRequestExplicitTypeChange()
    {
        var context = CreateContext(
            systemValues:
            [
                System("SD_MONACO", SystemBId, "SLIDING_DOOR", "SLIDING", "VENECIA MONACO"),
                System("SW_MONZA", SystemDId, "SLIDING_WINDOW", "SLIDING", "MONZA")
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_SYSTEM",
                contextItemId: ItemAId,
                requestedValue: "ventana corrediza Monza",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    System: new RequirementChatRequestedSystemAttributes(
                        FunctionalType: "SLIDING_WINDOW",
                        Operation: "SLIDING",
                        CommercialName: "MONZA"))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal(SystemDId, action.ResolvedCatalogEntity!.Id);
        Assert.Equal("SW_MONZA", action.ResolvedCatalogEntity.Code);
    }

    [Fact]
    public async Task RequirementChatAction_FinishAttributesResolveColorAndTexture()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_FINISH",
                contextItemId: ItemAId,
                requestedValue: "negro mate",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Finish: new RequirementChatRequestedFinishAttributes(
                        Color: "BLACK",
                        Texture: "MATTE"))),
            TestContext.Current.CancellationToken);

        var action = Assert.Single(result.Plan!.Actions);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan.Status);
        Assert.Equal(FinishAId, action.ResolvedCatalogEntity!.Id);
        Assert.Equal("BLACK_MATTE", action.ResolvedCatalogEntity.Code);
    }

    [Fact]
    public async Task RequirementChatAction_ConfirmWithoutPricingAppliesSelectionAndReturnsNotYetPriced()
    {
        var context = CreateContext(hasPricing: false);
        var plan = await ReadyPlanAsync(context, "CHANGE_FINISH", ItemAId, "WHITE_MATTE");

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", result.Plan!.Status);
        Assert.Equal("NOT_YET_PRICED", result.Plan.PricingStatus);
        Assert.Equal(1, context.Selection.Count);
        Assert.Equal(0, context.Pricing.RepriceCount);
        Assert.Equal(FinishBId, context.Reader.Item(ItemAId).Selected!.Finish!.Id);
    }

    [Theory]
    [InlineData("CHANGE_GLASS", "TEMP_8")]
    [InlineData("CHANGE_QUANTITY", null)]
    [InlineData("CHANGE_DIMENSIONS", null)]
    public async Task RequirementChatAction_ConfirmItemMutationsWithPricingUseReprice(string actionType, string? requestedValue)
    {
        var context = CreateContext(hasPricing: true);
        var command = actionType switch
        {
            "CHANGE_QUANTITY" => Command(actionType, contextItemId: ItemAId, quantity: 3),
            "CHANGE_DIMENSIONS" => Command(actionType, contextItemId: ItemAId, widthMm: 1200, heightMm: 2100),
            _ => Command(actionType, contextItemId: ItemAId, requestedValue: requestedValue)
        };
        var planResult = await context.Plan.ExecuteAsync(command, TestContext.Current.CancellationToken);

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, planResult.Plan!.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", result.Plan!.Status);
        Assert.Equal(1, context.Pricing.RepriceCount);
        Assert.Equal(0, context.Selection.Count);
        var item = context.Reader.Item(ItemAId);
        if (actionType == "CHANGE_GLASS")
        {
            Assert.Equal(GlassBId, item.Selected!.Glass!.Id);
        }
        else if (actionType == "CHANGE_QUANTITY")
        {
            Assert.Equal(3, item.ManualQuantityOverride);
            Assert.Equal(3, item.EffectiveQuantity);
        }
        else
        {
            Assert.Equal(1200, item.ManualWidthMmOverride);
            Assert.Equal(2100, item.ManualHeightMmOverride);
        }
    }

    [Fact]
    public async Task RequirementChatAction_ExcludeItemWithExistingPricingUsesInclusionAndAttemptsTotalPricing()
    {
        var context = CreateContext(hasPricing: true);
        var plan = await ReadyPlanAsync(context, "EXCLUDE_ITEM", ItemAId, null);

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", result.Plan!.Status);
        Assert.Equal(1, context.Inclusion.Count);
        Assert.Equal(1, context.Pricing.PriceRequirementCount);
        Assert.False(context.Reader.Item(ItemAId).IsIncluded);
    }

    [Fact]
    public async Task RequirementChatAction_MultiTargetExcludeCreatesOnePlanWithTwoActionsAndNoWritesBeforeConfirm()
    {
        var context = CreateContext(hasPricing: true);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "EXCLUDE_ITEM",
                scope: "REQUIREMENT",
                targetReferences: ["V-01", "V-02"]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        Assert.True(result.Plan.RequiresConfirmation);
        Assert.Equal(2, result.Plan.Actions.Count);
        Assert.Equal([ItemAId, ItemBId], result.Plan.Actions.Select(action => action.TargetTechnicalProposalItemId).ToArray());
        Assert.All(result.Plan.Actions, action => Assert.Equal("EXCLUDE_ITEM", action.ActionType));
        Assert.Equal(0, context.Inclusion.Count);
        Assert.Equal(0, context.Pricing.PriceRequirementCount);
        Assert.True(context.Reader.Item(ItemAId).IsIncluded);
        Assert.True(context.Reader.Item(ItemBId).IsIncluded);
    }

    [Fact]
    public async Task RequirementChatAction_ConfirmMultiTargetExcludeAppliesBothAndPricesOnce()
    {
        var context = CreateContext(hasPricing: true);
        var plan = await ReadyPlanAsync(
            context,
            "EXCLUDE_ITEM",
            ["V-01", "V-02"],
            null);

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", result.Plan!.Status);
        Assert.Equal("PRICING_UPDATED", result.Plan.PricingStatus);
        Assert.Equal(2, context.Inclusion.Count);
        Assert.Equal(1, context.Pricing.PriceRequirementCount);
        Assert.Equal(0, context.Pricing.RepriceCount);
        Assert.False(context.Reader.Item(ItemAId).IsIncluded);
        Assert.False(context.Reader.Item(ItemBId).IsIncluded);
        Assert.Contains("PRICING_MODE=FULL", result.Plan.ExecutionReasons);
        Assert.Contains("ACTION_COUNT=2", result.Plan.ExecutionReasons);
    }

    [Fact]
    public async Task RequirementChatAction_MultiTargetChangeSystemCreatesOneActionPerTarget()
    {
        var context = CreateContext(
            systemValues:
            [
                System("K70", SystemAId),
                System("S50", SystemBId)
            ]);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_SYSTEM",
                scope: "REQUIREMENT",
                targetReferences: ["V-01", "V-02"],
                requestedValue: "S50"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        Assert.Equal(2, result.Plan.Actions.Count);
        Assert.All(result.Plan.Actions, action =>
        {
            Assert.Equal("CHANGE_SYSTEM", action.ActionType);
            Assert.Equal(SystemBId, action.ResolvedCatalogEntity!.Id);
        });
        Assert.Equal(0, context.Selection.Count);
    }

    [Fact]
    public async Task RequirementChatAction_MultiTargetChangeGlassCreatesOneActionPerTarget()
    {
        var context = CreateContext();

        var result = await context.Plan.ExecuteAsync(
            Command(
                "CHANGE_GLASS",
                scope: "REQUIREMENT",
                targetReferences: ["V-01", "V-02"],
                requestedValue: "vidrio templado de 8 mm",
                requestedAttributes: new RequirementChatRequestedAttributes(
                    Glass: new RequirementChatRequestedGlassAttributes(
                        Composition: "TEMPERED",
                        OuterThicknessMm: 8m))),
            TestContext.Current.CancellationToken);

        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        Assert.Equal(2, result.Plan.Actions.Count);
        Assert.All(result.Plan.Actions, action => Assert.Equal(GlassBId, action.ResolvedCatalogEntity!.Id));
    }

    [Fact]
    public async Task RequirementChatAction_MultiTargetMissingReferenceDoesNotCreateConfirmablePartialPlan()
    {
        var context = CreateContext(hasPricing: true);

        var result = await context.Plan.ExecuteAsync(
            Command(
                "EXCLUDE_ITEM",
                scope: "REQUIREMENT",
                targetReferences: ["V-01", "V-99"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("NEEDS_CLARIFICATION", result.Plan!.Status);
        Assert.False(result.Plan.RequiresConfirmation);
        Assert.Equal(2, result.Plan.Actions.Count);
        Assert.Contains(result.Plan.Actions, action => action.TargetTechnicalProposalItemId == ItemAId);
        Assert.Contains(result.Plan.Actions, action => action.ValidationReasons.Contains("TARGET_REFERENCE_NOT_FOUND"));
        Assert.Equal(0, context.Inclusion.Count);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(0, context.Pricing.PriceRequirementCount);
    }

    [Fact]
    public async Task RequirementChatAction_ConfirmMultiTargetSystemBatchPricesOnceAndIsIdempotent()
    {
        var context = CreateContext(
            hasPricing: true,
            systemValues:
            [
                System("K70", SystemAId),
                System("S50", SystemBId)
            ]);
        var plan = await ReadyPlanAsync(
            context,
            "CHANGE_SYSTEM",
            ["V-01", "V-02"],
            "S50");

        var first = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);
        var second = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED", first.Plan!.Status);
        Assert.Equal("EXECUTED", second.Plan!.Status);
        Assert.Equal(2, context.Selection.Count);
        Assert.Equal(1, context.Pricing.PriceRequirementCount);
        Assert.Equal(0, context.Pricing.RepriceCount);
        Assert.Equal(SystemBId, context.Reader.Item(ItemAId).Selected!.System!.Id);
        Assert.Equal(SystemBId, context.Reader.Item(ItemBId).Selected!.System!.Id);
    }

    [Fact]
    public async Task RequirementChatAction_ConcurrentConfirmMultiTargetBatchDoesNotDuplicateSideEffects()
    {
        var context = CreateContext(hasPricing: true, priceRequirementDelay: TimeSpan.FromMilliseconds(75));
        var plan = await ReadyPlanAsync(
            context,
            "EXCLUDE_ITEM",
            ["V-01", "V-02"],
            null);
        var command = new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId);

        await Task.WhenAll(
            context.Confirm.ExecuteAsync(command, TestContext.Current.CancellationToken),
            context.Confirm.ExecuteAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(2, context.Inclusion.Count);
        Assert.Equal(1, context.Pricing.PriceRequirementCount);
        Assert.False(context.Reader.Item(ItemAId).IsIncluded);
        Assert.False(context.Reader.Item(ItemBId).IsIncluded);
    }

    [Fact]
    public async Task RequirementChatAction_MultiTargetPricingFailureLeavesExecutedWithPricingPending()
    {
        var context = CreateContext(hasPricing: true, priceRequirementFailure: true);
        var plan = await ReadyPlanAsync(
            context,
            "EXCLUDE_ITEM",
            ["V-01", "V-02"],
            null);

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED_WITH_PRICING_PENDING", result.Plan!.Status);
        Assert.Equal("PRICING_PENDING", result.Plan.PricingStatus);
        Assert.Contains("PRICING_FAILED_QueryError", result.Plan.ExecutionReasons);
        Assert.Equal(2, context.Inclusion.Count);
        Assert.Equal(1, context.Pricing.PriceRequirementCount);
    }

    [Fact]
    public async Task RequirementChatAction_RepriceFailureAppliesSelectionAndLeavesPricingPending()
    {
        var context = CreateContext(hasPricing: true, repriceFailure: true);
        var plan = await ReadyPlanAsync(context, "CHANGE_SYSTEM", ItemAId, "K72");

        var result = await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.Equal("EXECUTED_WITH_PRICING_PENDING", result.Plan!.Status);
        Assert.Equal("PRICING_PENDING", result.Plan.PricingStatus);
        Assert.Contains("REPRICE_FAILED_QueryError", result.Plan.ExecutionReasons);
        Assert.Equal(1, context.Pricing.RepriceCount);
        Assert.Equal(1, context.Selection.Count);
        Assert.Equal(SystemBId, context.Reader.Item(ItemAId).Selected!.System!.Id);
    }

    [Fact]
    public async Task RequirementChatAction_ConcurrentConfirmDoesNotDuplicateSideEffects()
    {
        var context = CreateContext(hasPricing: true, repriceDelay: TimeSpan.FromMilliseconds(75));
        var plan = await ReadyPlanAsync(context, "CHANGE_SYSTEM", ItemAId, "K72");
        var command = new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId);

        await Task.WhenAll(
            context.Confirm.ExecuteAsync(command, TestContext.Current.CancellationToken),
            context.Confirm.ExecuteAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(1, context.Pricing.RepriceCount);
        Assert.Equal(0, context.Selection.Count);
        Assert.Equal(SystemBId, context.Reader.Item(ItemAId).Selected!.System!.Id);
    }

    [Fact]
    public async Task RequirementChatAction_ConfirmRefreshesProposalAfterMutation()
    {
        var context = CreateContext(hasPricing: false);
        var plan = await ReadyPlanAsync(context, "CHANGE_SYSTEM", ItemAId, "K72");

        await context.Confirm.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(RequirementId, plan.PlanId),
            TestContext.Current.CancellationToken);

        Assert.True(context.Reader.GetCount >= 2);
        var refreshed = await context.Reader.GetAsync(RequirementId, TestContext.Current.CancellationToken);
        Assert.Equal(SystemBId, refreshed.Proposal!.Items.Single(item => item.ItemId == ItemAId).Selected!.System!.Id);
    }

    private static PlanRequirementChatActionCommand Command(
        string actionType,
        Guid? contextItemId = null,
        string? scope = null,
        string? targetReference = null,
        string? requestedValue = null,
        int? quantity = null,
        int? widthMm = null,
        int? heightMm = null,
        IReadOnlyList<string>? targetReferences = null,
        RequirementChatRequestedAttributes? requestedAttributes = null) =>
        new(
            RequirementId,
            null,
            null,
            contextItemId,
            scope,
            actionType,
            null,
            targetReference,
            targetReferences,
            requestedValue,
            quantity,
            widthMm,
            heightMm,
            "chat request",
            requestedAttributes);

    private static async Task<ChatActionPlanReadModel> ReadyPlanAsync(
        Context context,
        string actionType,
        Guid itemId,
        string? requestedValue)
    {
        var result = await context.Plan.ExecuteAsync(
            Command(actionType, contextItemId: itemId, requestedValue: requestedValue),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        return result.Plan;
    }

    private static async Task<ChatActionPlanReadModel> ReadyPlanAsync(
        Context context,
        string actionType,
        IReadOnlyList<string> targetReferences,
        string? requestedValue)
    {
        var result = await context.Plan.ExecuteAsync(
            Command(
                actionType,
                scope: "REQUIREMENT",
                targetReferences: targetReferences,
                requestedValue: requestedValue),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("READY_FOR_CONFIRMATION", result.Plan!.Status);
        return result.Plan;
    }

    private static Context CreateContext(
        bool hasPricing = false,
        bool duplicateReference = false,
        bool repriceFailure = false,
        bool priceRequirementFailure = false,
        TimeSpan? repriceDelay = null,
        TimeSpan? priceRequirementDelay = null,
        IReadOnlyList<ProductSystemCatalogReadModel>? systemValues = null,
        IReadOnlyList<GlassTypeCatalogReadModel>? glassValues = null,
        IReadOnlyList<FinishTypeCatalogReadModel>? finishValues = null)
    {
        var reader = new FakeTechnicalProposalReader(CreateProposal(duplicateReference));
        var selection = new FakeSelectionExecutor(reader);
        var inclusion = new FakeInclusionExecutor(reader);
        var pricing = new FakePricingExecutor(reader)
        {
            FailReprice = repriceFailure,
            FailPriceRequirement = priceRequirementFailure,
            RepriceDelay = repriceDelay ?? TimeSpan.Zero,
            PriceRequirementDelay = priceRequirementDelay ?? TimeSpan.Zero
        };
        var store = new InMemoryRequirementChatActionPlanStore(new FixedTimeProvider(At));
        var repository = Substitute.For<IRequirementRepository>();
        repository.GetCurrentPricingSnapshotAsync(RequirementId, Arg.Any<CancellationToken>())
            .Returns(hasPricing
                ? RequirementPricingSnapshot.Create(
                    RequirementId,
                    ProposalId,
                    1,
                    "COP",
                    "PUBLIC_QUOTED_ITEM_PRICES",
                    100m,
                    100m,
                    At)
                : null);
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns(systemValues ?? [System("K70", SystemAId), System("K72", SystemBId)]);
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns(glassValues ?? [Glass("TEMP_6", GlassAId, true), Glass("TEMP_8", GlassBId, true)]);
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();
        finishes.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns(finishValues ?? [Finish("BLACK_MATTE", FinishAId), Finish("WHITE_MATTE", FinishBId)]);

        var plan = new PlanRequirementChatActionService(
            reader,
            systems,
            glasses,
            finishes,
            store,
            new FixedTimeProvider(At));
        var confirm = new ConfirmRequirementChatActionService(
            store,
            repository,
            selection,
            inclusion,
            pricing,
            reader);
        return new Context(reader, selection, inclusion, pricing, plan, confirm);
    }

    private static RequirementTechnicalProposalReadModel CreateProposal(bool duplicateReference) =>
        new(
            RequirementId,
            ProposalId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CURRENT",
            "ESSENTIAL",
            new RequirementTechnicalProposalCommercialConfirmationReadModel(
                "PENDING_CONFIRMATION",
                null,
                null),
            At,
            duplicateReference ? 2 : 2,
            2,
            0,
            2,
            0,
            2,
            2,
            new RequirementTechnicalProposalReadinessReadModel(
                "READY",
                true,
                true,
                0,
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, int>()),
            [
                Item(ItemAId, "V-01", 1),
                Item(ItemBId, duplicateReference ? "V-01" : "V-02", 2)
            ]);

    private static RequirementTechnicalProposalItemReadModel Item(Guid id, string reference, int sequence) =>
        new(
            id,
            Guid.NewGuid(),
            "AI_EXTRACTED",
            $"element-{sequence}",
            sequence,
            reference,
            "Puerta vidriera",
            "Door",
            1,
            1000,
            2000,
            null,
            null,
            null,
            1,
            1000,
            2000,
            2m,
            true,
            null,
            null,
            null,
            0.95m,
            "EXPLICIT",
            null,
            new RequirementTechnicalProposalSuggestedReadModel(
                OptionSystem(SystemAId, "K70"),
                OptionGlass(GlassAId, "TEMP_6"),
                OptionFinish(FinishAId, "BLACK_MATTE")),
            null,
            "UNCONFIRMED",
            new RequirementTechnicalProposalAlternativesReadModel([], [], []),
            new RequirementTechnicalProposalConfidenceReadModel(0.9m, 0.9m, 0.9m, 0.9m),
            false,
            [],
            [],
            [],
            [],
            true,
            true,
            new RequirementTechnicalProposalItemReadinessReadModel("READY", 0, 0, []),
            new RequirementTechnicalProposalHistoricalEvidenceReadModel("AVAILABLE", 1, 0.9m, 0.9m, []),
            new RequirementTechnicalProposalVisualModelReadModel(
                "1",
                "SUGGESTED_SYSTEM",
                new RequirementTechnicalProposalVisualSystemReadModel(SystemAId, "K70", "Sistema K70"),
                "SLIDING_DOOR",
                "SLIDING",
                "RECTANGULAR",
                1000,
                2000,
                1,
                [],
                [],
                [],
                false,
                []),
            new RequirementTechnicalProposalTraceReadModel(
                "3831",
                null,
                "SLIDING_DOOR",
                "SLIDING",
                "templado 6 mm",
                "templado",
                "templado",
                6m,
                "negro",
                "PAINTED",
                "negro",
                "BLACK",
                [],
                "RECTANGULAR"),
            []);

    private static ProductSystemCatalogReadModel System(
        string code,
        Guid id,
        string functionalType = "SLIDING_DOOR",
        string operation = "SLIDING",
        string? commercialName = null) =>
        new(
            id,
            code,
            $"{functionalType} {commercialName ?? code}",
            $"{operation} {functionalType} {commercialName ?? code}",
            commercialName ?? code,
            functionalType,
            code,
            operation,
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel Glass(string code, Guid id, bool selectable) =>
        new(
            id,
            code,
            $"Cristal {code}",
            null,
            true,
            null,
            Family: code.StartsWith("LAM_", StringComparison.Ordinal) ? "LAMINATED" : "MONOLITHIC",
            Composition: code.StartsWith("TEMP_", StringComparison.Ordinal) ? "TEMPERED" : "RAW",
            OuterThicknessMm: code switch
            {
                "TEMP_5" => 5m,
                "TEMP_6" => 6m,
                "TEMP_8" => 8m,
                "TEMP_10" => 10m,
                _ => null
            },
            IsSelectable: selectable);

    private static GlassTypeCatalogReadModel GlassCatalog(
        string code,
        Guid id,
        string name,
        string family,
        string? composition,
        string? treatment,
        decimal? outerThicknessMm,
        decimal? innerThicknessMm,
        decimal? chamberThicknessMm) =>
        new(
            id,
            code,
            name,
            null,
            true,
            null,
            Family: family,
            Composition: composition,
            Treatment: treatment,
            OuterThicknessMm: outerThicknessMm,
            InnerThicknessMm: innerThicknessMm,
            ChamberThicknessMm: chamberThicknessMm,
            IsSelectable: true);

    private static FinishTypeCatalogReadModel Finish(string code, Guid id) =>
        new(
            id,
            code,
            $"Acabado {code}",
            "PAINTED",
            code.StartsWith("BLACK", StringComparison.Ordinal) ? "BLACK" : "WHITE",
            "MATTE",
            "PAINTED",
            null,
            "ALUMINUM",
            true,
            false,
            true);

    private static RequirementTechnicalProposalSystemOptionReadModel OptionSystem(Guid id, string code) =>
        new(id, code, $"Sistema {code}", $"Sistema tecnico {code}", code, "SLIDING_DOOR", code, "SERIE", "ESSENTIAL", "STANDARD");

    private static RequirementTechnicalProposalGlassOptionReadModel OptionGlass(Guid id, string code) =>
        new(id, code, $"Cristal {code}", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static RequirementTechnicalProposalFinishOptionReadModel OptionFinish(Guid id, string code) =>
        new(id, code, $"Acabado {code}", "PAINTED", code, "MATTE", "PAINTED", null, "ALUMINUM");

    private sealed record Context(
        FakeTechnicalProposalReader Reader,
        FakeSelectionExecutor Selection,
        FakeInclusionExecutor Inclusion,
        FakePricingExecutor Pricing,
        PlanRequirementChatActionService Plan,
        ConfirmRequirementChatActionService Confirm);

    private sealed class FakeTechnicalProposalReader(RequirementTechnicalProposalReadModel proposal)
        : IRequirementChatTechnicalProposalReader
    {
        public RequirementTechnicalProposalReadModel Current { get; private set; } = proposal;
        public int GetCount { get; private set; }

        public Task<GetRequirementTechnicalProposalResult> GetAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult(GetRequirementTechnicalProposalResult.Success(Current));
        }

        public RequirementTechnicalProposalItemReadModel Item(Guid itemId) =>
            Current.Items.Single(item => item.ItemId == itemId);

        public void UpdateItem(Guid itemId, Func<RequirementTechnicalProposalItemReadModel, RequirementTechnicalProposalItemReadModel> update)
        {
            Current = Current with
            {
                Items = Current.Items.Select(item => item.ItemId == itemId ? update(item) : item).ToArray()
            };
        }
    }

    private sealed class FakeSelectionExecutor(FakeTechnicalProposalReader reader)
        : IRequirementChatSelectionExecutor
    {
        public int Count { get; private set; }

        public Task<UpdateRequirementTechnicalProposalItemSelectionResult> ExecuteAsync(
            UpdateRequirementTechnicalProposalItemSelectionCommand command,
            CancellationToken cancellationToken)
        {
            Count++;
            ApplySelection(reader, command.ItemId, command.SystemId, command.GlassId, command.FinishId, command.Quantity, command.WidthMillimeters, command.HeightMillimeters);
            return Task.FromResult(UpdateRequirementTechnicalProposalItemSelectionResult.Success(
                new RequirementTechnicalProposalItemSelectionReadModel(command.TechnicalProposalId, command.ItemId, "MODIFIED", At, Guid.NewGuid(), null, null, null)));
        }
    }

    private sealed class FakeInclusionExecutor(FakeTechnicalProposalReader reader)
        : IRequirementChatInclusionExecutor
    {
        public int Count { get; private set; }

        public Task<UpdateRequirementTechnicalProposalItemInclusionResult> ExecuteAsync(
            UpdateRequirementTechnicalProposalItemInclusionCommand command,
            CancellationToken cancellationToken)
        {
            Count++;
            reader.UpdateItem(command.ItemId, item => item with { IsIncluded = command.IsIncluded });
            return Task.FromResult(UpdateRequirementTechnicalProposalItemInclusionResult.Success(
                new RequirementTechnicalProposalItemInclusionReadModel(ProposalId, command.ItemId, command.IsIncluded, null, null, command.Reason, 1)));
        }
    }

    private sealed class FakePricingExecutor(FakeTechnicalProposalReader reader)
        : IRequirementChatPricingExecutor
    {
        public int PriceRequirementCount { get; private set; }
        public int RepriceCount { get; private set; }
        public bool FailReprice { get; init; }
        public bool FailPriceRequirement { get; init; }
        public TimeSpan RepriceDelay { get; init; }
        public TimeSpan PriceRequirementDelay { get; init; }

        public Task<PriceRequirementTechnicalProposalResult> PriceRequirementAsync(
            PriceRequirementTechnicalProposalCommand command,
            CancellationToken cancellationToken)
        {
            PriceRequirementCount++;
            if (PriceRequirementDelay > TimeSpan.Zero)
            {
                return DelayedPriceRequirementAsync(command, cancellationToken);
            }

            if (FailPriceRequirement)
            {
                return Task.FromResult(PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.QueryError));
            }

            return Task.FromResult(PriceRequirementTechnicalProposalResult.Success(
                PricingReadModel(command.RequirementId)));
        }

        private async Task<PriceRequirementTechnicalProposalResult> DelayedPriceRequirementAsync(
            PriceRequirementTechnicalProposalCommand command,
            CancellationToken cancellationToken)
        {
            await Task.Delay(PriceRequirementDelay, cancellationToken);
            return FailPriceRequirement
                ? PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.QueryError)
                : PriceRequirementTechnicalProposalResult.Success(
                    PricingReadModel(command.RequirementId));
        }

        public async Task<RepriceRequirementTechnicalProposalItemResult> RepriceItemAsync(
            RepriceRequirementTechnicalProposalItemCommand command,
            CancellationToken cancellationToken)
        {
            RepriceCount++;
            if (RepriceDelay > TimeSpan.Zero)
            {
                await Task.Delay(RepriceDelay, cancellationToken);
            }

            if (FailReprice)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure.QueryError);
            }

            ApplySelection(reader, command.TechnicalProposalItemId, command.SystemId, command.GlassTypeId, command.FinishTypeId, command.Quantity, command.WidthMillimeters, command.HeightMillimeters);
            return RepriceRequirementTechnicalProposalItemResult.Success(
                new RepriceRequirementTechnicalProposalItemReadModel(
                    command.RequirementId,
                    ProposalId,
                    command.TechnicalProposalItemId,
                    command.SystemId,
                    command.GlassTypeId,
                    command.FinishTypeId,
                    PricingItem(command.TechnicalProposalItemId),
                    100m,
                    100m,
                    0m));
        }
    }

    private static void ApplySelection(
        FakeTechnicalProposalReader reader,
        Guid itemId,
        Guid? systemId,
        Guid? glassId,
        Guid? finishId,
        int? quantity,
        int? widthMm,
        int? heightMm) =>
        reader.UpdateItem(itemId, item =>
        {
            var selected = item.Selected
                ?? new RequirementTechnicalProposalSelectedReadModel(
                    item.Suggested.System,
                    item.Suggested.Glass,
                    item.Suggested.Finish,
                    At,
                    Guid.NewGuid());
            return item with
            {
                Selected = selected with
                {
                    System = systemId is null ? selected.System : OptionSystem(systemId.Value, systemId == SystemBId ? "K72" : "K70"),
                    Glass = glassId is null ? selected.Glass : OptionGlass(glassId.Value, glassId == GlassBId ? "TEMP_8" : "TEMP_6"),
                    Finish = finishId is null ? selected.Finish : OptionFinish(finishId.Value, finishId == FinishBId ? "WHITE_MATTE" : "BLACK_MATTE")
                },
                SelectionState = "MODIFIED",
                ManualQuantityOverride = quantity ?? item.ManualQuantityOverride,
                EffectiveQuantity = quantity ?? item.EffectiveQuantity,
                ManualWidthMmOverride = widthMm ?? item.ManualWidthMmOverride,
                EffectiveWidthMm = widthMm ?? item.EffectiveWidthMm,
                ManualHeightMmOverride = heightMm ?? item.ManualHeightMmOverride,
                EffectiveHeightMm = heightMm ?? item.EffectiveHeightMm
            };
        });

    private static RequirementTechnicalProposalPricingReadModel PricingReadModel(Guid requirementId) =>
        new(
            requirementId,
            ProposalId,
            "COP",
            "PUBLIC_QUOTED_ITEM_PRICES",
            1,
            1,
            0,
            0,
            new TechnicalProposalPricingMoneyRange(90m, 100m, 110m),
            true,
            false,
            [],
            [],
            [PricingItem(ItemAId)]);

    private static TechnicalProposalPricingItemReadModel PricingItem(Guid itemId) =>
        new(
            itemId,
            Guid.NewGuid(),
            "AI_EXTRACTED",
            "element-1",
            1,
            "V-01",
            "Puerta vidriera",
            "PRICEABLE",
            "SELECTED",
            1,
            2m,
            new TechnicalProposalPricingMoneyRange(90m, 100m, 110m),
            new TechnicalProposalPricingMoneyRange(90m, 100m, 110m),
            0.9m,
            "HIGH",
            false,
            [],
            [],
            [],
            []);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
