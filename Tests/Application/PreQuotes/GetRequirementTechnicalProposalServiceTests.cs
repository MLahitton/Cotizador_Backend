using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetRequirementTechnicalProposalServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithCurrentProposal_ReturnsTechnicalProposalForFrontend()
    {
        var context = CreateContext(withProposal: true);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var proposal = Assert.IsType<RequirementTechnicalProposalReadModel>(
            result.Proposal);
        Assert.Equal(context.Requirement.Id, proposal.RequirementId);
        Assert.Equal(1, proposal.ItemCount);
        Assert.Equal("ESSENTIAL", proposal.CommercialLine);
        Assert.Equal(
            "PENDING_CONFIRMATION",
            proposal.CommercialConfirmation.State);
        Assert.Null(proposal.CommercialConfirmation.ConfirmedAtUtc);
        Assert.Equal(0, proposal.ItemsRequiringReview);
        Assert.Equal(1, proposal.TechnicallyCompleteItems);
        Assert.Equal(1, proposal.PriceableItems);

        var item = Assert.Single(proposal.Items);
        Assert.Equal("PV-06", item.Reference);
        Assert.Equal("element-pv06", item.ElementId);
        Assert.Equal(1, item.Quantity);
        Assert.Null(item.ManualQuantityOverride);
        Assert.Equal(1, item.EffectiveQuantity);
        Assert.Equal(3740, item.WidthMm);
        Assert.Equal(2500, item.HeightMm);
        Assert.Equal(9.35m, item.AreaM2);
        Assert.False(item.RequiresReview);
        Assert.Equal(["SYSTEM_RULE"], item.SystemResolutionReasons);
        Assert.Equal(["GLASS_RULE"], item.GlassResolutionReasons);
        Assert.Equal(["FINISH_RULE"], item.FinishResolutionReasons);
        Assert.True(item.IsTechnicallyComplete);
        Assert.True(item.IsPriceable);
        Assert.Equal(0.83m, item.Confidence.Overall);

        Assert.NotNull(item.Suggested.System);
        Assert.Equal("K70", item.Suggested.System!.Code);
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            item.Suggested.System.DisplayName);
        Assert.NotNull(item.Suggested.Glass);
        Assert.Equal("TEMP_6", item.Suggested.Glass!.Code);
        Assert.Equal(
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC",
            item.Suggested.Glass.DisplayName);
        Assert.NotNull(item.Suggested.Finish);
        Assert.Equal("BLACK_MATTE", item.Suggested.Finish!.Code);
        Assert.Equal(
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            item.Suggested.Finish.DisplayName);
        Assert.Null(item.Selected);
        Assert.Equal("UNCONFIRMED", item.SelectionState);

        var systemAlternative = Assert.Single(item.Alternatives.Systems);
        Assert.Equal("K72", systemAlternative.Option.Code);
        Assert.Equal(1, systemAlternative.Rank);
        Assert.Equal(["SYSTEM_ALTERNATIVE"], systemAlternative.Reasons);
        var glassAlternative = Assert.Single(item.Alternatives.Glass);
        Assert.Equal("TEMP_8", glassAlternative.Option.Code);
        var finishAlternative = Assert.Single(item.Alternatives.Finishes);
        Assert.Equal("WHITE_MATTE", finishAlternative.Option.Code);

        Assert.Equal("AVAILABLE", item.HistoricalEvidence.Status);
        Assert.Equal(3, item.HistoricalEvidence.SupportCount);
        Assert.Equal(0.91m, item.HistoricalEvidence.BestSimilarity);
        var example = Assert.Single(item.HistoricalEvidence.Examples);
        Assert.Equal("candidate-1", example.CandidateId);
        Assert.Equal("PV-06", example.HistoricalReference);

        Assert.Equal("3831", item.Trace.RequestedSystemRaw);
        Assert.Equal("templado", item.Trace.GlassTypeNormalized);
        Assert.Equal(6m, item.Trace.GlassThicknessMm);
        Assert.Equal("negro pintura al horno", item.Trace.FinishRawDescription);
        Assert.Equal(["POCKET"], item.Trace.SpecialFeatures);
        Assert.Equal("RECTANGULAR", item.Trace.GeometryType);
        Assert.Equal("SUGGESTED_SYSTEM", item.VisualModel.Source);
        Assert.Equal("K70", item.VisualModel.System!.Code);
        Assert.Equal("SLIDING_DOOR", item.VisualModel.FunctionalType);
        Assert.Equal("SLIDING", item.VisualModel.Operation);
        Assert.Equal("RECTANGULAR", item.VisualModel.GeometryType);
        Assert.Equal(3740, item.VisualModel.WidthMm);
        Assert.Equal(2500, item.VisualModel.HeightMm);
        Assert.Equal(1, item.VisualModel.Quantity);
        Assert.Equal(["POCKET"], item.VisualModel.SpecialFeatures);

        var evidence = Assert.Single(item.Evidence);
        Assert.Null(evidence.PageNumber);
        Assert.Equal("Xlsx", evidence.SourceType);
        Assert.Equal("Cotizacion", evidence.SheetName);
        Assert.Equal("A12:H12", evidence.CellRange);
        Assert.Equal("CUADRO VENTANAS NIVEL 1 (3).pdf", evidence.SourceFileName);
        Assert.Equal("Nivel 1", evidence.ContextLabel);
    }

    [Fact]
    public async Task Execute_WithReadyReadinessAndLegacyStaleFlags_UsesCurrentReadinessForSummaryCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty(item, "RequiresReview", true);
                SetPrivateProperty(item, "IsTechnicallyComplete", false);
                SetPrivateProperty(item, "IsPriceable", false);
                SetPrivateProperty(item, "ReviewReasons", Array.Empty<string>());
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(1, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(1, result.Proposal.PriceableItems);
        Assert.Equal("READY", Assert.Single(result.Proposal.Items).Readiness.State);
    }

    [Fact]
    public async Task Execute_WithBlockedReadiness_UsesCurrentBlockersForSummaryCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty<Guid?>(item, "SuggestedSystemId", null);
                SetPrivateProperty(item, "RequiresReview", false);
                SetPrivateProperty(item, "IsTechnicallyComplete", true);
                SetPrivateProperty(item, "IsPriceable", true);
                SetPrivateProperty(item, "ReviewReasons", Array.Empty<string>());
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(0, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(0, result.Proposal.PriceableItems);
        Assert.Equal("BLOCKED", Assert.Single(result.Proposal.Items).Readiness.State);
    }

    [Fact]
    public async Task Execute_WithWarningReadiness_CountsReviewWithoutBlockingTechnicalCompletenessOrPricing()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty(item, "RequiresReview", false);
                SetPrivateProperty(item, "IsTechnicallyComplete", false);
                SetPrivateProperty(item, "IsPriceable", false);
                SetPrivateProperty(
                    item,
                    "ReviewReasons",
                    new[] { "INVALID_EVIDENCE_LOCATION" });
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(1, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(1, result.Proposal.PriceableItems);
        Assert.Equal("REVIEW_REQUIRED", Assert.Single(result.Proposal.Items).Readiness.State);
    }

    [Fact]
    public async Task Execute_WithQuantityOverrideRestoringReadiness_UsesEffectiveQuantityForSummaryCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty<int?>(item.ExtractedItem, "Quantity", null);
                item.ApplyManualDataOverride(2, null, null);
                SetPrivateProperty(item, "RequiresReview", true);
                SetPrivateProperty(item, "IsTechnicallyComplete", false);
                SetPrivateProperty(item, "IsPriceable", false);
                SetPrivateProperty(item, "ReviewReasons", Array.Empty<string>());
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(1, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(1, result.Proposal.PriceableItems);
        Assert.Equal(2, Assert.Single(result.Proposal.Items).EffectiveQuantity);
    }

    [Fact]
    public async Task Execute_WithDimensionOverrideRestoringReadiness_UsesEffectiveMeasurementsForSummaryCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty<int?>(item.ExtractedItem, "WidthMillimeters", null);
                item.ApplyManualDataOverride(null, 1200, null);
                SetPrivateProperty(item, "RequiresReview", true);
                SetPrivateProperty(item, "IsTechnicallyComplete", false);
                SetPrivateProperty(item, "IsPriceable", false);
                SetPrivateProperty(item, "ReviewReasons", Array.Empty<string>());
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(1, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(1, result.Proposal.PriceableItems);
        Assert.Equal(1200, Assert.Single(result.Proposal.Items).EffectiveWidthMm);
    }

    [Fact]
    public async Task Execute_WithConfigurationChangeRestoringReadiness_UsesCurrentSelectionForSummaryCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                var selectedSystemId = item.SystemAlternatives.Single().ProductSystemId;
                SetPrivateProperty<Guid?>(item, "SuggestedSystemId", null);
                SetPrivateProperty(item, "RequiresReview", true);
                SetPrivateProperty(item, "IsTechnicallyComplete", false);
                SetPrivateProperty(item, "IsPriceable", false);
                SetPrivateProperty(
                    item,
                    "ReviewReasons",
                    new[] { "SYSTEM_NOT_RESOLVED" });
                item.Select(
                    selectedSystemId,
                    item.SuggestedGlassTypeId,
                    item.SuggestedFinishTypeId,
                    UserId,
                    At.AddMinutes(10));
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Proposal!.ItemsRequiringReview);
        Assert.Equal(1, result.Proposal.TechnicallyCompleteItems);
        Assert.Equal(1, result.Proposal.PriceableItems);
        Assert.Equal("READY", Assert.Single(result.Proposal.Items).Readiness.State);
    }

    [Fact]
    public async Task Execute_WithSelectedProposal_ReturnsSelectedSeparatelyFromSuggested()
    {
        var context = CreateContext(withProposal: true, withSelected: true);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Proposal!.Items);
        Assert.NotNull(item.Suggested.System);
        Assert.NotNull(item.Selected);
        Assert.Equal("K70", item.Suggested.System!.Code);
        Assert.Equal("K72", item.Selected!.System!.Code);
        Assert.Equal("TEMP_8", item.Selected.Glass!.Code);
        Assert.Equal("WHITE_MATTE", item.Selected.Finish!.Code);
        Assert.Equal("MODIFIED", item.SelectionState);
        Assert.Equal(At.AddMinutes(5), item.Selected.SelectedAtUtc);
        Assert.Equal(UserId, item.Selected.SelectedByUserId);
        Assert.Equal("SELECTED_SYSTEM", item.VisualModel.Source);
        Assert.Equal("K72", item.VisualModel.System!.Code);
    }

    [Fact]
    public async Task Execute_WithFixedSegment_ReturnsFixedVisualPanel()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "FIXED");
                SetPrivateProperty<string?>(extracted, "Operation", null);
                SetPrivateProperty<string?>(extracted, "OpeningDirection", null);
                SetPrivateProperty<int?>(item, "BaseWidthMillimeters", 1000);
                SetPrivateProperty<int?>(item, "BaseHeightMillimeters", 2000);
                AddSegment(extracted, 1, "FIXED", 1000, 2000);
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var visual = Assert.Single(result.Proposal!.Items).VisualModel;
        var panel = Assert.Single(visual.Panels);
        Assert.Equal("SIMPLE", panel.Kind);
        Assert.Equal("FIXED", panel.Role);
        Assert.False(panel.IsMovable);
        Assert.Empty(panel.SubPanels);
        Assert.Equal(1000, visual.WidthMm);
        Assert.Equal(2000, visual.HeightMm);
        Assert.Equal(1m, panel.WidthRatio);
        Assert.Equal(1m, panel.HeightRatio);
        Assert.False(visual.RequiresReview);
    }

    [Fact]
    public async Task Execute_WithProjectingAndFixedSegments_ReturnsOrderedVisualPanels()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "PROJECTING");
                SetPrivateProperty(extracted, "Operation", "PROJECTING");
                SetPrivateProperty(extracted, "OpeningDirection", "OUTWARD");
                AddSegment(extracted, 1, "FIXED", null, null);
                AddSegment(extracted, 2, "PROJECTING", null, null, "PROJECTING");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var panels = Assert.Single(result.Proposal!.Items).VisualModel.Panels;
        Assert.Collection(
            panels,
            panel =>
            {
                Assert.Equal(1, panel.Index);
                Assert.Equal("SIMPLE", panel.Kind);
                Assert.Equal("FIXED", panel.Role);
                Assert.False(panel.IsMovable);
                Assert.Empty(panel.SubPanels);
            },
            panel =>
            {
                Assert.Equal(2, panel.Index);
                Assert.Equal("SIMPLE", panel.Kind);
                Assert.Equal("PROJECTING", panel.Role);
                Assert.True(panel.IsMovable);
                Assert.Equal("PROJECTING", panel.Operation);
                Assert.Equal("OUTWARD", panel.OpeningDirection);
                Assert.Empty(panel.SubPanels);
            });

        Assert.DoesNotContain(
            "VISUAL_SUBPANEL_LAYOUT_UNRESOLVED",
            Assert.Single(result.Proposal.Items).VisualModel.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithExplicitCompositeSegment_ReturnsCompositeVisualPanel()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "PROJECTING");
                SetPrivateProperty(extracted, "Operation", "PROJECTING");
                SetPrivateProperty(extracted, "OpeningDirection", "OUTWARD");
                SetPrivateProperty<int?>(item, "BaseWidthMillimeters", 1300);
                SetPrivateProperty<int?>(item, "BaseHeightMillimeters", 1800);
                AddSegment(
                    extracted,
                    1,
                    "COMPOSITE",
                    1300,
                    1800,
                    "FIXED:1400+PROJECTING:400");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var visual = Assert.Single(result.Proposal!.Items).VisualModel;
        var panel = Assert.Single(visual.Panels);
        Assert.Equal("COMPOSITE", panel.Kind);
        Assert.Equal("PROJECTING", panel.Role);
        Assert.Equal(1300, panel.WidthMm);
        Assert.Equal(1800, panel.HeightMm);
        Assert.Equal(1m, panel.WidthRatio);
        Assert.Equal(1m, panel.HeightRatio);
        Assert.True(panel.IsMovable);
        Assert.Null(panel.OpeningDirection);
        Assert.False(visual.RequiresReview);

        Assert.Collection(
            panel.SubPanels,
            subPanel =>
            {
                Assert.Equal(1, subPanel.Index);
                Assert.Equal("SIMPLE", subPanel.Kind);
                Assert.Equal("FIXED", subPanel.Role);
                Assert.False(subPanel.IsMovable);
                Assert.Null(subPanel.OpeningDirection);
                Assert.Equal(1400m / 1800m, subPanel.HeightRatio);
                Assert.Empty(subPanel.SubPanels);
            },
            subPanel =>
            {
                Assert.Equal(2, subPanel.Index);
                Assert.Equal("SIMPLE", subPanel.Kind);
                Assert.Equal("PROJECTING", subPanel.Role);
                Assert.True(subPanel.IsMovable);
                Assert.Equal("OUTWARD", subPanel.OpeningDirection);
                Assert.Equal(400m / 1800m, subPanel.HeightRatio);
                Assert.Empty(subPanel.SubPanels);
            });
    }

    [Fact]
    public async Task Execute_WithMultipleExplicitCompositeSegments_PreservesOrder()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "PROJECTING");
                SetPrivateProperty(extracted, "Operation", "PROJECTING");
                SetPrivateProperty(extracted, "OpeningDirection", "OUTWARD");
                AddSegment(extracted, 1, "FIXED", null, null);
                AddSegment(
                    extracted,
                    2,
                    "COMPOSITE",
                    null,
                    1800,
                    "FIXED:1400+PROJECTING:400");
                AddSegment(extracted, 3, "FIXED", null, null);
                AddSegment(
                    extracted,
                    4,
                    "MODULE",
                    null,
                    1800,
                    "FIXED:1200+PROJECTING:600");
                AddSegment(extracted, 5, "FIXED", null, null);
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var panels = Assert.Single(result.Proposal!.Items).VisualModel.Panels;
        Assert.Equal([1, 2, 3, 4, 5], panels.Select(panel => panel.Index));
        Assert.Equal(
            ["SIMPLE", "COMPOSITE", "SIMPLE", "COMPOSITE", "SIMPLE"],
            panels.Select(panel => panel.Kind));
        Assert.Equal(2, panels[1].SubPanels.Count);
        Assert.Equal(2, panels[3].SubPanels.Count);
    }

    [Fact]
    public async Task Execute_WithSlidingSegments_ReturnsMovablePanels()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "SLIDING_WINDOW");
                SetPrivateProperty(extracted, "Operation", "SLIDING");
                AddSegment(extracted, 1, "SLIDING", null, null, "SLIDING");
                AddSegment(extracted, 2, "SLIDING", null, null, "SLIDING");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var panels = Assert.Single(result.Proposal!.Items).VisualModel.Panels;
        Assert.Equal(2, panels.Count);
        Assert.All(panels, panel =>
        {
            Assert.Equal("SIMPLE", panel.Kind);
            Assert.Equal("SLIDING", panel.Role);
            Assert.True(panel.IsMovable);
            Assert.Empty(panel.SubPanels);
        });
    }

    [Fact]
    public async Task Execute_WithOperationOnlySlidingSegment_UsesPanelCounts()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "SLIDING_DOOR");
                SetPrivateProperty(extracted, "Operation", "SLIDING");
                SetPrivateProperty<int?>(extracted, "PanelCount", 4);
                SetPrivateProperty<int?>(extracted, "MovablePanelCount", 4);
                SetPrivateProperty<int?>(extracted, "FixedPanelCount", 0);
                AddSegment(extracted, 1, "SLIDING", null, null, "SLIDING");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var visual = Assert.Single(result.Proposal!.Items).VisualModel;
        Assert.Equal(4, visual.Panels.Count);
        Assert.All(visual.Panels, panel =>
        {
            Assert.Equal("SIMPLE", panel.Kind);
            Assert.Equal("SLIDING", panel.Role);
            Assert.True(panel.IsMovable);
            Assert.Equal("SLIDING", panel.Operation);
            Assert.Empty(panel.SubPanels);
        });
        Assert.True(visual.RequiresReview);
        Assert.Contains(
            "VISUAL_PANEL_ORDER_UNRESOLVED",
            visual.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithHingedSegment_PreservesOpeningDirection()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "SWING_DOOR");
                SetPrivateProperty(extracted, "Operation", "HINGED");
                SetPrivateProperty(extracted, "OpeningDirection", "RIGHT");
                AddSegment(extracted, 1, "HINGED", null, null, "HINGED");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var panel = Assert.Single(Assert.Single(result.Proposal!.Items).VisualModel.Panels);
        Assert.Equal("SIMPLE", panel.Kind);
        Assert.Equal("HINGED", panel.Role);
        Assert.True(panel.IsMovable);
        Assert.Equal("RIGHT", panel.OpeningDirection);
        Assert.Empty(panel.SubPanels);
    }

    [Fact]
    public async Task Execute_WithFoldingSegments_UsesStableSequenceOrder()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "FunctionalType", "FOLDING_DOOR");
                SetPrivateProperty(extracted, "Operation", "FOLDING");
                AddSegment(extracted, 2, "FOLDING", null, null, "FOLDING");
                AddSegment(extracted, 1, "FOLDING", null, null, "FOLDING");
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var panels = Assert.Single(result.Proposal!.Items).VisualModel.Panels;
        Assert.Equal([1, 2], panels.Select(panel => panel.Index).ToArray());
        Assert.All(panels, panel =>
        {
            Assert.Equal("SIMPLE", panel.Kind);
            Assert.Equal("FOLDING", panel.Role);
            Assert.True(panel.IsMovable);
            Assert.Empty(panel.SubPanels);
        });
    }

    [Fact]
    public async Task Execute_WithCornerGeometryWithoutTopology_ReturnsVisualReview()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                Assert.NotNull(item.ExtractedItem);
                var extracted = item.ExtractedItem!;
                SetPrivateProperty(extracted, "GeometryType", "CORNER");
                AddSegment(extracted, 1, "FIXED", null, null);
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var visual = Assert.Single(result.Proposal!.Items).VisualModel;
        Assert.Equal("CORNER", visual.GeometryType);
        Assert.True(visual.RequiresReview);
        Assert.Contains(
            "VISUAL_CORNER_TOPOLOGY_UNRESOLVED",
            visual.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithNoSelectedOrSuggestedSystem_UsesExtractionOnly()
    {
        var context = CreateContext(
            withProposal: true,
            configureProposal: proposal =>
            {
                var item = proposal.Items.Single();
                SetPrivateProperty<Guid?>(item, "SuggestedSystemId", null);
                Assert.NotNull(item.ExtractedItem);
                AddSegment(item.ExtractedItem!, 1, "FIXED", null, null);
            });

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        var visual = Assert.Single(result.Proposal!.Items).VisualModel;
        Assert.Equal("EXTRACTION_ONLY", visual.Source);
        Assert.Null(visual.System);
        Assert.True(visual.RequiresReview);
        Assert.Contains("VISUAL_SYSTEM_UNRESOLVED", visual.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithExtractedQuantity_ReturnsQuantityAsEffectiveWhenNoManualOverride()
    {
        var context = CreateContext(withProposal: true, extractedQuantity: 25);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Proposal!.Items);
        Assert.Equal(25, item.Quantity);
        Assert.Null(item.ManualQuantityOverride);
        Assert.Equal(25, item.EffectiveQuantity);
    }

    [Fact]
    public async Task Execute_WithManualQuantityOverride_ReturnsOverrideAsEffectiveAndKeepsOriginal()
    {
        var context = CreateContext(
            withProposal: true,
            extractedQuantity: 25,
            manualQuantityOverride: 7);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Proposal!.Items);
        Assert.Equal(25, item.Quantity);
        Assert.Equal(7, item.ManualQuantityOverride);
        Assert.Equal(7, item.EffectiveQuantity);
    }

    [Fact]
    public async Task Execute_WithNoCurrentProposal_ReturnsNotFoundFailure()
    {
        var context = CreateContext(withProposal: false);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetRequirementTechnicalProposalFailure.TechnicalProposalNotFound,
            result.Failure);
    }

    [Fact]
    public void ReadModel_DoesNotExposePricingFields()
    {
        var forbidden = typeof(RequirementTechnicalProposalReadModel).Assembly
            .GetTypes()
            .Where(type => type.Namespace
                == "Application.PreQuotes.GetRequirementTechnicalProposal")
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .Where(name =>
                name.Contains("Minimum", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Expected", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Maximum", StringComparison.OrdinalIgnoreCase)
                || name.Contains("UnitPrice", StringComparison.OrdinalIgnoreCase)
                || (name.Contains("Total", StringComparison.OrdinalIgnoreCase)
                    && name != "TotalProposalItemCount"))
            .ToArray();

        Assert.Empty(forbidden);
    }

    private static Context CreateContext(
        bool withProposal,
        bool withSelected = false,
        int extractedQuantity = 1,
        int? manualQuantityOverride = null,
        Action<RequirementTechnicalProposal>? configureProposal = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();

        var user = User.CreateFromGoogle(
            "user@example.com",
            "User",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Client",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            UserId,
            At);
        var project = ProjectEntity.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            null,
            UserId,
            At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2020-0001", null, At);
        var requirement = Requirement.Create(preQuote.Id, UserId, RequirementCommercialLine.Essential, At);

        var system = ProductSystem(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "K70",
            "VENECIA NAPOLES");
        var alternativeSystem = ProductSystem(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "K72",
            "MONACO");
        var glass = Glass(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "TEMP_6",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC",
            6m);
        var alternativeGlass = Glass(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "TEMP_8",
            "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            8m);
        var finish = Finish(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "BLACK_MATTE",
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            "BLACK");
        var alternativeFinish = Finish(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "WHITE_MATTE",
            "ALUCOLOR POLIESTER BLANCO",
            "WHITE");

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindByIdAsync(requirement.Id, Arg.Any<CancellationToken>())
            .Returns(requirement);
        var proposal = withProposal
            ? CreateProposal(
                requirement,
                requirement.Id,
                system.Id,
                alternativeSystem.Id,
                glass.GlassTypeId,
                alternativeGlass.GlassTypeId,
                finish.Id,
                alternativeFinish.Id,
                withSelected,
                extractedQuantity,
                manualQuantityOverride)
            : null;
        if (proposal is not null)
        {
            configureProposal?.Invoke(proposal);
        }

        requirements.GetCurrentTechnicalProposalAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(proposal);
        requirements.ListFilesByRequirementIdAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns([
                RequirementFile.Create(
                    Guid.NewGuid(),
                    "CUADRO VENTANAS NIVEL 1 (3).pdf",
                    "application/pdf",
                    100,
                    "requirements/r/source-1/original.pdf",
                    At),
                RequirementFile.Create(
                    Guid.NewGuid(),
                    "CUADRO VENTANAS NIVEL 2 (3).pdf",
                    "application/pdf",
                    100,
                    "requirements/r/source-2/original.pdf",
                    At.AddSeconds(1))
            ]);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);
        systems.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([system, alternativeSystem]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns([glass, alternativeGlass]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([finish, alternativeFinish]);

        var service = new GetRequirementTechnicalProposalService(
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients,
            systems,
            glasses,
            finishes);

        return new Context(service, requirement, proposal);
    }

    private static RequirementTechnicalProposal CreateProposal(
        Requirement requirement,
        Guid requirementId,
        Guid systemId,
        Guid alternativeSystemId,
        Guid glassId,
        Guid alternativeGlassId,
        Guid finishId,
        Guid alternativeFinishId,
        bool withSelected,
        int extractedQuantity,
        int? manualQuantityOverride)
    {
        var attemptId = Guid.NewGuid();
        var extraction = RequirementExtractionResult.Create(
            attemptId,
            "AI2-1.0",
            "Ai2",
            "{\"requirement\":{},\"elements\":[]}",
            1,
            0,
            0,
            0,
            "ai2_requirement_extraction",
            1000,
            At);
        var item = RequirementExtractedItem.Create(
            extraction.Id,
            "element-pv06",
            1,
            "PV-06",
            "Puerta vidriera",
            StructuredElementType.Door,
            extractedQuantity,
            3740,
            2500,
            9.35m,
            0.92m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "SLIDING_DOOR",
            "SLIDING",
            null,
            null,
            null,
            "TWO_PANELS",
            null,
            null,
            ["POCKET"],
            "RECTANGULAR",
            "3831",
            "3831",
            "templado 6 mm",
            "templado",
            "templado",
            6m,
            null,
            null,
            null,
            null,
            "monolitico",
            null,
            null,
            false,
            "negro pintura al horno",
            "PAINTED",
            null,
            "BLACK",
            null,
            "MATTE",
            null,
            false,
            At);
        var evidence = RequirementExtractedItemEvidence.Create(
            item.Id,
            null,
            EvidenceSourceType.Xlsx,
            "PV-06 Puerta vidriera",
            "Cotizacion",
            "A12:H12",
            "source-1",
            0.95m,
            RequirementExtractionValueStatus.Explicit,
            At);
        AddPrivateList(item, "_evidence", evidence);

        var proposal = RequirementTechnicalProposal.Create(
            requirementId,
            extraction.Id,
            attemptId,
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var proposalItem = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            item.Id,
            systemId,
            glassId,
            finishId,
            item.Sequence,
            item.Reference,
            item.Description,
            item.ElementType,
            item.Quantity,
            item.WidthMillimeters,
            item.HeightMillimeters,
            0.83m,
            0.90m,
            0.88m,
            0.83m,
            false,
            true,
            true,
            [],
            ["SYSTEM_RULE"],
            ["GLASS_RULE"],
            ["FINISH_RULE"],
            3,
            0.91m,
            0.87m,
            "AVAILABLE",
            At);
        SetPrivateProperty(proposalItem, "ExtractedItem", item);
        proposalItem.ApplyManualDataOverride(
            manualQuantityOverride,
            null,
            null);
        proposalItem.AddSystemAlternative(
            RequirementTechnicalProposalSystemAlternative.Create(
                proposalItem.Id,
                alternativeSystemId,
                1,
                0.71m,
                ["SYSTEM_ALTERNATIVE"]));
        proposalItem.AddGlassAlternative(
            RequirementTechnicalProposalGlassAlternative.Create(
                proposalItem.Id,
                alternativeGlassId,
                1,
                0.69m,
                ["GLASS_ALTERNATIVE"]));
        proposalItem.AddFinishAlternative(
            RequirementTechnicalProposalFinishAlternative.Create(
                proposalItem.Id,
                alternativeFinishId,
                1,
                0.68m,
                ["FINISH_ALTERNATIVE"]));
        proposalItem.AddHistoricalExample(
            RequirementTechnicalProposalHistoricalExample.Create(
                proposalItem.Id,
                "candidate-1",
                "SG943",
                "PV-06",
                0.91m,
                ["system", "glass"],
                ["finish"],
                "Comparable tecnico cercano."));
        if (withSelected)
        {
            proposalItem.Select(
                alternativeSystemId,
                alternativeGlassId,
                alternativeFinishId,
                UserId,
                At.AddMinutes(5));
        }

        proposal.AddItem(proposalItem);

        return proposal;
    }

    private static ProductSystemCatalogReadModel ProductSystem(
        Guid id,
        string code,
        string family) =>
        new(
            id,
            code,
            $"PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO {family}",
            $"PUERTA CORREDIZA SISTEMA {family}",
            family,
            "SLIDING_DOOR",
            family,
            "SERIE 70",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel Glass(
        Guid id,
        string code,
        string name,
        decimal thickness) =>
        new(
            id,
            code,
            name,
            null,
            true,
            null,
            Family: "MONOLITHIC",
            Composition: "TEMPERED",
            Treatment: "TEMPERED",
            OuterThicknessMm: thickness,
            IsSelectable: true);

    private static FinishTypeCatalogReadModel Finish(
        Guid id,
        string code,
        string name,
        string color) =>
        new(
            id,
            code,
            name,
            "PAINTED",
            color,
            "MATTE",
            "PAINTED",
            null,
            "ALUMINUM",
            true,
            false,
            true);

    private static void SetPrivateProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static void AddPrivateList<T>(
        object target,
        string fieldName,
        T value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        var list = Assert.IsType<List<T>>(field!.GetValue(target));
        list.Add(value);
    }

    private static void AddSegment(
        RequirementExtractedItem item,
        int sequence,
        string role,
        int? widthMillimeters,
        int? heightMillimeters,
        string? operation = null)
    {
        item.AddSegment(RequirementExtractedItemSegment.Create(
            item.Id,
            sequence,
            role,
            widthMillimeters,
            heightMillimeters,
            null,
            operation,
            "RECTANGULAR",
            $"{role} panel",
            "source-1",
            EvidenceSourceType.Native,
            1,
            null,
            null,
            0.90m,
            RequirementExtractionValueStatus.Explicit,
            At));
    }

    private sealed record Context(
        GetRequirementTechnicalProposalService Service,
        Requirement Requirement,
        RequirementTechnicalProposal? Proposal);
}
