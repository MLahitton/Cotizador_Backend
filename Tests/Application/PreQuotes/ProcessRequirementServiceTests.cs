using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Application.PreQuotes.ProcessRequirement;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ProcessRequirementServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private const string PdfContentType = "application/pdf";
    private const string PngContentType = "image/png";

    [Fact]
    public async Task Execute_WithValidRequirement_CallsAi2AndPersistsExtraction()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        RequirementProcessingAttempt? attempt = null;
        RequirementExtractionResult? extraction = null;
        RequirementExtractedItem? extractedItem = null;
        RequirementExtractedItemEvidence? extractedEvidence = null;
        RequirementTechnicalProposal? proposal = null;
        context.Requirements.When(repository => repository.AddProcessingAttempt(
                Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => attempt = call.Arg<RequirementProcessingAttempt>());
        context.Requirements.When(repository => repository.AddExtractionResult(
                Arg.Any<RequirementExtractionResult>()))
            .Do(call => extraction = call.Arg<RequirementExtractionResult>());
        context.Requirements.When(repository => repository.AddExtractedItem(
                Arg.Any<RequirementExtractedItem>()))
            .Do(call => extractedItem = call.Arg<RequirementExtractedItem>());
        context.Requirements.When(repository => repository.AddExtractedItemEvidence(
                Arg.Any<RequirementExtractedItemEvidence>()))
            .Do(call => extractedEvidence = call.Arg<RequirementExtractedItemEvidence>());
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.Completed, result.Attempt!.Outcome);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        Assert.NotNull(attempt);
        Assert.NotNull(extraction);
        Assert.Equal(attempt!.Id, extraction!.RequirementProcessingAttemptId);
        Assert.Equal("Ai2", extraction.Provider);
        Assert.Equal(15, extraction.ItemCount);
        Assert.Equal(0, extraction.ItemsRequiringReview);
        Assert.Equal("{\"requirement\":{},\"elements\":[]}", extraction.PayloadJson);
        Assert.NotNull(extractedItem);
        Assert.Equal(extraction.Id, extractedItem!.RequirementExtractionResultId);
        Assert.Equal("element-pv06", extractedItem.Ai2ElementId);
        Assert.Equal("PV-06", extractedItem.Reference);
        Assert.Equal("SLIDING_DOOR", extractedItem.FunctionalType);
        Assert.Equal("3831", extractedItem.RequestedSystemRaw);
        Assert.Equal("templado", extractedItem.GlassTypeNormalized);
        Assert.Equal(6m, extractedItem.GlassThicknessMm);
        Assert.Equal("negro pintura al horno", extractedItem.FinishRawDescription);
        Assert.Equal("PAINTED", extractedItem.FinishNormalizedType);
        Assert.Equal("BLACK", extractedItem.FinishColorNormalized);
        Assert.NotNull(extractedEvidence);
        Assert.Equal(extractedItem.Id, extractedEvidence!.RequirementExtractedItemId);
        Assert.Equal("A12:H12", extractedEvidence.CellRange);
        Assert.NotNull(proposal);
        var proposalItem = Assert.Single(proposal!.Items);
        Assert.NotNull(proposalItem.SuggestedSystemId);
        Assert.NotNull(proposalItem.SuggestedGlassTypeId);
        Assert.NotNull(proposalItem.SuggestedFinishTypeId);
        Assert.False(proposalItem.RequiresReview);
        Assert.True(proposalItem.IsTechnicallyComplete);
        Assert.True(proposalItem.IsPriceable);
        await context.Requirements.Received(3).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithMultipleFiles_SendsFilesProjectAndRequirementToAi2()
    {
        DocumentProcessingClientRequest? captured = null;
        bool? allStreamsWereReadableDuringAiCall = null;
        var first = File("source.pdf", PdfContentType);
        var second = File("photo.png", PngContentType);
        var context = CreateContext("success", first, second);
        context.Ai2.ProcessAsync(
                Arg.Do<DocumentProcessingClientRequest>(request =>
                {
                    captured = request;
                    allStreamsWereReadableDuringAiCall =
                        request.Files.All(file => file.Content.CanRead);
                }),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DocumentProcessingClientResult.Success(
                CreateResponse(call.Arg<DocumentProcessingClientRequest>()))));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(first.Id, captured!.DocumentId);
        Assert.Equal(context.Project.Id, captured.ProjectId);
        Assert.Equal(context.Requirement.Id, captured.RequirementId);
        Assert.Equal(2, captured.Files.Count);
        Assert.Equal([first.Id, second.Id], captured.Files.Select(file => file.DocumentId));
        Assert.Equal(["source.pdf", "photo.png"], captured.Files.Select(file => file.FileName));
        Assert.True(allStreamsWereReadableDuringAiCall);
    }

    [Fact]
    public async Task Execute_WithExplicitSegments_PersistsSegmentsAndUsesThemForGlass()
    {
        RequirementTechnicalProposal? proposal = null;
        var persistedSegments = new List<RequirementExtractedItemSegment>();
        var context = CreateContext(
            "tempered_segments_4100",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddExtractedItemSegment(
                Arg.Any<RequirementExtractedItemSegment>()))
            .Do(call => persistedSegments.Add(
                call.Arg<RequirementExtractedItemSegment>()));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, persistedSegments.Count);
        Assert.Equal([2050, 2050], persistedSegments.Select(segment =>
            segment.WidthMillimeters));
        Assert.Equal([2800, 2800], persistedSegments.Select(segment =>
            segment.HeightMillimeters));
        Assert.All(persistedSegments, segment =>
            Assert.Equal("FIXED", segment.Role));
        var proposalItem = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_10", SuggestedGlassCode(context, proposalItem));
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromSubmodules,
            proposalItem.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.JointGlassRule,
            proposalItem.GlassResolutionReasons);
    }

    [Theory]
    [InlineData("assembly_sliding_fixed_grille", "K70", false,
        "SLIDING", "FIXED", "GRILLE")]
    [InlineData("assembly_projecting_fixed", "S35", false,
        "PROJECTING", "FIXED")]
    [InlineData("assembly_swing_fixed", "3890", false,
        "SWING", "FIXED")]
    [InlineData("assembly_fixed_grille", "K40", false,
        "FIXED", "GRILLE")]
    [InlineData("assembly_grille_only", "SG_LOUVER", false,
        "GRILLE")]
    [InlineData("assembly_sliding_swing", null, true,
        "SLIDING", "SWING")]
    [InlineData("assembly_sliding_window_grille", "K50", false,
        "SLIDING", "GRILLE")]
    [InlineData("assembly_sliding_window_lower_fixed", "K50", false,
        "SLIDING", "FIXED")]
    [InlineData("assembly_sliding_window_mobile_over_threshold", "K70", true,
        "SLIDING", "FIXED")]
    [InlineData("assembly_sliding_window_unresolved_geometry", null, true,
        "SLIDING", "FIXED")]
    [InlineData("assembly_sliding_door_fixed", "K70", false,
        "SLIDING", "FIXED")]
    [InlineData("assembly_shower_sliding_fixed", null, true,
        "SLIDING", "FIXED")]
    [InlineData("assembly_skylight_fixed", null, true,
        "FIXED")]
    [InlineData("success", "K70", false)]
    public async Task Execute_WithAssemblyComponents_ResolvesPrimarySystem(
        string scenario,
        string? expectedSystemCode,
        bool expectedReview,
        params string[] expectedSegmentRoles)
    {
        RequirementTechnicalProposal? proposal = null;
        var persistedSegments = new List<RequirementExtractedItemSegment>();
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddExtractedItemSegment(
                Arg.Any<RequirementExtractedItemSegment>()))
            .Do(call => persistedSegments.Add(
                call.Arg<RequirementExtractedItemSegment>()));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSegmentRoles, persistedSegments
            .Select(segment => segment.Role)
            .ToArray());
        var proposalItem = Assert.Single(proposal!.Items);
        Assert.Equal(expectedSystemCode, SuggestedSystemCode(context, proposalItem));
        Assert.Equal(expectedReview, proposalItem.RequiresReview);
        if (scenario == "assembly_sliding_swing")
        {
            Assert.Contains(
                SgTechnicalSelectionReviewReasons
                    .AssemblyMultipleMovableTypesRequiresReview,
                proposalItem.ReviewReasons);
        }

        if (scenario == "assembly_sliding_window_lower_fixed")
        {
            Assert.DoesNotContain(
                SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
                proposalItem.SystemResolutionReasons);
        }

        if (scenario == "assembly_sliding_window_mobile_over_threshold")
        {
            Assert.Contains(
                SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
                proposalItem.SystemResolutionReasons);
        }

        if (scenario == "assembly_sliding_window_unresolved_geometry")
        {
            Assert.Contains(
                SgTechnicalSelectionReviewReasons.PrimaryComponentGeometryUnresolved,
                proposalItem.ReviewReasons);
            Assert.DoesNotContain(
                SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
                proposalItem.SystemResolutionReasons);
        }
    }

    [Fact]
    public async Task Execute_WithAi2RequiresReview_CompletesAttemptAndMarksRequirementProcessed()
    {
        var context = CreateContext("requires_review", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Attempt!.Outcome);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        Assert.Equal(1, result.Attempt.Summary!.ItemsRequiringReview);
    }

    [Fact]
    public async Task Execute_WithAi2RawGlassSignal_DoesNotRequireBackendCatalogCode()
    {
        var context = CreateContext("success", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await context.Ai2.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
        context.Requirements.Received(1).AddExtractionResult(
            Arg.Is<RequirementExtractionResult>(extraction =>
                extraction.ItemCount == 15
                && extraction.Provider == "Ai2"));
    }

    [Fact]
    public async Task Execute_WithMissingGlassSignal_AppliesAuditableTemp5Default()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext("missing_glass", File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.NotNull(item.SuggestedGlassTypeId);
        Assert.False(item.RequiresReview);
        Assert.DoesNotContain("HISTORICAL_DEFAULT_GLASS", item.ReviewReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassLineTempered,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithMissingFinishSignal_AppliesPp13DefaultWithoutReview()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext("missing_finish", File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.NotNull(item.SuggestedFinishTypeId);
        Assert.False(item.RequiresReview);
        Assert.DoesNotContain("FINISH_NOT_SPECIFIED", item.ReviewReasons);
        Assert.Contains("HISTORICAL_DEFAULT_FINISH", item.FinishResolutionReasons);
    }

    [Theory]
    [InlineData("tempered_2400", "TEMP_5")]
    [InlineData("tempered_2401", "TEMP_6")]
    [InlineData("tempered_2601", "TEMP_8")]
    [InlineData("tempered_2801", "TEMP_10")]
    [InlineData("tempered_narrow_2700", "TEMP_6")]
    [InlineData("tempered_joint_1951", "TEMP_10")]
    [InlineData("tempered_joint_5000", "TEMP_10")]
    [InlineData("tempered_three_panel_4000", "TEMP_6")]
    [InlineData("tempered_two_panel_3000", "TEMP_6")]
    [InlineData("tempered_three_panel_5000", "TEMP_6")]
    [InlineData("tempered_single_pane_3000", "TEMP_10")]
    [InlineData("tempered_pane_1950", "TEMP_6")]
    [InlineData("tempered_pane_1951", "TEMP_10")]
    [InlineData("tempered_horizontal_split_2780", "TEMP_5")]
    [InlineData("tempered_evidence_vertical_uniform_900_2700", "TEMP_5")]
    [InlineData("tempered_evidence_vertical_nonuniform_1000_2700", "TEMP_5")]
    [InlineData("tempered_evidence_horizontal_4000", "TEMP_5")]
    [InlineData("tempered_evidence_horizontal_joint_4500", "TEMP_10")]
    [InlineData("tempered_evidence_inconsistent_900_2700", "TEMP_8")]
    [InlineData("tempered_evidence_ambiguous_4000", "TEMP_5")]
    [InlineData("skylight_roof_geometry_5110_920", "TEMP_10")]
    [InlineData("skylight_roof_segments_1000_1500", "TEMP_5")]
    [InlineData("skylight_missing_length_920_2700", "TEMP_8")]
    [InlineData("pocket_leaf_2000_total_4000", "TEMP_10")]
    [InlineData("pocket_leaf_1500_total_3000", "TEMP_8")]
    [InlineData("pocket_two_leaves_1200_total_4800", "TEMP_8")]
    [InlineData("pocket_overall_4000_panel_1", "TEMP_8")]
    [InlineData("pocket_two_cuerpos_4800", "TEMP_8")]
    [InlineData("sliding_non_pocket_3000_2700", "TEMP_10")]
    [InlineData("tempered_explicit_two_panes_3300", "TEMP_5")]
    [InlineData("tempered_two_panel_no_distribution_3300", "TEMP_5")]
    [InlineData("tempered_explicit_joint_pane_2050", "TEMP_10")]
    [InlineData("tempered_explicit_two_panes_3800", "TEMP_8")]
    [InlineData("tempered_explicit_widths_4000", "TEMP_10")]
    [InlineData("tempered_heterogeneous_panes", "TEMP_8")]
    [InlineData("tempered_explicit_uniform_widths_3000", "TEMP_6")]
    [InlineData("tempered_explicit_narrow_width_500", "TEMP_6")]
    [InlineData("tempered_single_2780", "TEMP_8")]
    [InlineData("tempered_single_pane_1200_2700", "TEMP_8")]
    [InlineData("tempered_unknown_3000", "TEMP_6")]
    public async Task Execute_WithTemperedLine_AppliesConfirmedThicknessRules(
        string scenario,
        string expectedGlassCode)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal(expectedGlassCode, SuggestedGlassCode(context, item));
        Assert.Contains(
            GlassResolutionReasonCodes.GlassLineTempered,
            item.GlassResolutionReasons);
    }

    [Theory]
    [InlineData("tempered_three_panel_4000")]
    [InlineData("tempered_two_panel_3000")]
    [InlineData("tempered_three_panel_5000")]
    [InlineData("tempered_two_panel_no_distribution_3300")]
    [InlineData("tempered_evidence_inconsistent_900_2700")]
    [InlineData("tempered_evidence_ambiguous_4000")]
    [InlineData("skylight_missing_length_920_2700")]
    [InlineData("skylight_roof_panelcount_6_total")]
    [InlineData("pocket_overall_4000_panel_1")]
    [InlineData("pocket_two_cuerpos_4800")]
    public async Task Execute_WithMultiPanelTemperedWidth_DoesNotApplyJointFromTotalWidth(
        string scenario)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.ReviewReasons);
    }

    [Theory]
    [InlineData("tempered_evidence_vertical_uniform_900_2700", false, false)]
    [InlineData("tempered_evidence_horizontal_4000", false, false)]
    [InlineData("tempered_evidence_horizontal_joint_4500", true, true)]
    public async Task Execute_WithExplicitEvidencePaneSegmentation_UsesEvidenceGeometry(
        string scenario,
        bool expectedJoint,
        bool expectedReview)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal(expectedReview, item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromEvidence,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        if (expectedJoint)
        {
            Assert.Contains(
                GlassResolutionReasonCodes.JointGlassRule,
                item.GlassResolutionReasons);
        }
        else
        {
            Assert.DoesNotContain(
                GlassResolutionReasonCodes.JointGlassRule,
                item.GlassResolutionReasons);
        }
    }

    [Fact]
    public async Task Execute_WithSkylightLengthWidthEvidence_UsesRoofGlassGeometry()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "skylight_roof_geometry_5110_920",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_10", SuggestedGlassCode(context, item));
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromRoofGeometry,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithSkylightWithoutLength_DoesNotUseArchitecturalHeightAsResolvedPane()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "skylight_missing_length_920_2700",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.True(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromRoofGeometry,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithSkylightTotalAndPanelCount_DoesNotDivideRoofByPanelCount()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "skylight_roof_panelcount_6_total",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.True(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromRoofGeometry,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
    }

    [Theory]
    [InlineData("pocket_leaf_2000_total_4000", true)]
    [InlineData("pocket_leaf_1500_total_3000", false)]
    [InlineData("pocket_two_leaves_1200_total_4800", false)]
    public async Task Execute_WithPocketLeafEvidence_UsesLeafGeometry(
        string scenario,
        bool expectedJoint)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromPocketLeaf,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        if (expectedJoint)
        {
            Assert.Contains(
                GlassResolutionReasonCodes.JointGlassRule,
                item.GlassResolutionReasons);
        }
        else
        {
            Assert.DoesNotContain(
                GlassResolutionReasonCodes.JointGlassRule,
                item.GlassResolutionReasons);
        }
    }

    [Fact]
    public async Task Execute_WithNonPocketSlidingDoor_KeepsConventionalGeometry()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "sliding_non_pocket_3000_2700",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_10", SuggestedGlassCode(context, item));
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromElement,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromPocketLeaf,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithExplicitUniformPaneWidths_UsesKnownDistribution()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_explicit_uniform_widths_3000",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_6", SuggestedGlassCode(context, item));
        Assert.False(item.RequiresReview);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromSubmodules,
            item.GlassResolutionReasons);
    }

    [Theory]
    [InlineData("tempered_explicit_two_panes_3300", "TEMP_5")]
    [InlineData("tempered_explicit_two_panes_3800", "TEMP_8")]
    public async Task Execute_WithExplicitPaneWidthsBelowJointThreshold_DoesNotApplyJoint(
        string scenario,
        string expectedGlassCode)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal(expectedGlassCode, SuggestedGlassCode(context, item));
        Assert.False(item.RequiresReview);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromSubmodules,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithUnknownPaneGeometry_DoesNotInventPanelsAndRequiresReview()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_unknown_3000",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_6", SuggestedGlassCode(context, item));
        Assert.True(item.RequiresReview);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithExplicitPaneWidths_AppliesJointFromPaneWidth()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_explicit_widths_4000",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_10", SuggestedGlassCode(context, item));
        Assert.True(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromSubmodules,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneHeterogeneousNeeds,
            item.ReviewReasons);
    }

    [Fact]
    public async Task Execute_WithSingleExplicitPaneOverJointThreshold_AppliesJoint()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_explicit_joint_pane_2050",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_10", SuggestedGlassCode(context, item));
        Assert.Contains(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithHeterogeneousPaneGeometry_SelectsCoveringGlassAndRequiresReview()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_heterogeneous_panes",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_8", SuggestedGlassCode(context, item));
        Assert.True(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneHeterogeneousNeeds,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneHeterogeneousNeeds,
            item.ReviewReasons);
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithExplicitNarrowPaneWidth_AppliesNarrowRule()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_explicit_narrow_width_500",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_6", SuggestedGlassCode(context, item));
        Assert.False(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.NarrowGlassHeightExtension,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromSubmodules,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithSinglePane_UsesElementPaneGeometry()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "tempered_single_pane_1200_2700",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("TEMP_8", SuggestedGlassCode(context, item));
        Assert.False(item.RequiresReview);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneDimensionsFromElement,
            item.GlassResolutionReasons);
    }

    [Theory]
    [InlineData("signature_laminated_normal", "LAM_4_4")]
    [InlineData("signature_laminated_5_5", "LAM_5_5")]
    [InlineData("signature_laminated_three_panel_5000", "LAM_4_4")]
    [InlineData("signature_laminated_explicit_widths_5000", "LAM_5_5")]
    public async Task Execute_WithLaminatedLine_AppliesConfirmedLaminatedRules(
        string scenario,
        string expectedGlassCode)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal(expectedGlassCode, SuggestedGlassCode(context, item));
        Assert.Contains(
            GlassResolutionReasonCodes.GlassLineLaminated,
            item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithMultiPanelLaminatedWidth_DoesNotApplyJointFromTotalWidth()
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(
            "signature_laminated_three_panel_5000",
            File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal("LAM_4_4", SuggestedGlassCode(context, item));
        Assert.DoesNotContain(
            GlassResolutionReasonCodes.JointGlassRule,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.GlassResolutionReasons);
        Assert.Contains(
            GlassResolutionReasonCodes.GlassPaneGeometryUnresolved,
            item.ReviewReasons);
    }

    [Theory]
    [InlineData("special_shower", "TEMP_8", GlassResolutionReasonCodes.SpecialGlassShower8Mm)]
    [InlineData("special_railing", "TEMP_10", GlassResolutionReasonCodes.SpecialGlassRailing10Mm)]
    public async Task Execute_WithSpecialGlassCase_AppliesSpecialGlassRule(
        string scenario,
        string expectedGlassCode,
        string expectedReason)
    {
        RequirementTechnicalProposal? proposal = null;
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));
        context.Requirements.When(repository => repository.AddTechnicalProposal(
                Arg.Any<RequirementTechnicalProposal>()))
            .Do(call => proposal = call.Arg<RequirementTechnicalProposal>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(proposal!.Items);
        Assert.Equal(expectedGlassCode, SuggestedGlassCode(context, item));
        Assert.Contains(expectedReason, item.GlassResolutionReasons);
    }

    [Fact]
    public async Task Execute_WithInvalidEvidenceLocation_DoesNotPersistInvalidEvidence()
    {
        var context = CreateContext(
            "invalid_evidence_location",
            File("source.pdf", PdfContentType));
        RequirementExtractedItem? extractedItem = null;
        context.Requirements.When(repository => repository.AddExtractedItem(
                Arg.Any<RequirementExtractedItem>()))
            .Do(call => extractedItem = call.Arg<RequirementExtractedItem>());

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Attempt!.Outcome);
        Assert.NotNull(extractedItem);
        Assert.True(extractedItem!.RequiresReview);
        Assert.Contains("INVALID_EVIDENCE_LOCATION", extractedItem.ReviewReasons);
        context.Requirements.DidNotReceive().AddExtractedItemEvidence(
            Arg.Any<RequirementExtractedItemEvidence>());
    }

    [Fact]
    public async Task Execute_WhenTechnicalProposalCatalogThrows_FinalizesFailureLifecycle()
    {
        var context = CreateContext(
            "product_system_query_error",
            File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProcessRequirementFailure.PersistenceError, result.Failure);
        Assert.NotNull(result.Attempt);
        Assert.Equal(
            DocumentProcessingState.Finished,
            result.Attempt!.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, result.Attempt.Outcome);
        Assert.Equal("REQUIREMENT_PERSISTENCE_ERROR", result.Attempt.ErrorCode);
        Assert.NotEqual(
            DocumentProcessingState.Processing,
            result.Attempt.ProcessingState);
        Assert.Equal(RequirementStatus.Failed, context.Requirement.Status);
        await context.Requirements.Received(1).FinalizeProcessingFailureAsync(
            context.Requirement.Id,
            result.Attempt.ProcessingAttemptId,
            "REQUIREMENT_PERSISTENCE_ERROR",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenRequirementIsProcessing_ReturnsConflict()
    {
        var context = CreateContext("already_processing", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProcessRequirementFailure.AlreadyProcessing, result.Failure);
        await context.Ai2.DidNotReceive().ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenRequirementAlreadyProcessed_AllowsReprocessing()
    {
        var context = CreateContext("processed", File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementStatus.Processed, context.Requirement.Status);
        await context.Ai2.Received(1).ProcessAsync(
            Arg.Any<DocumentProcessingClientRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not_found", ProcessRequirementFailure.RequirementNotFound)]
    [InlineData("no_files", ProcessRequirementFailure.NoFiles)]
    [InlineData("storage_error", ProcessRequirementFailure.StorageError)]
    [InlineData("ai_unavailable", ProcessRequirementFailure.AiServiceUnavailable)]
    [InlineData("ai_timeout", ProcessRequirementFailure.AiTimeout)]
    [InlineData("ai_invalid", ProcessRequirementFailure.AiInvalidResponse)]
    public async Task Execute_WithFailure_ReturnsExpectedFailure(
        string scenario,
        ProcessRequirementFailure expected)
    {
        var context = CreateContext(scenario, File("source.pdf", PdfContentType));

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
        if (expected is ProcessRequirementFailure.StorageError
            or ProcessRequirementFailure.AiServiceUnavailable
            or ProcessRequirementFailure.AiTimeout
            or ProcessRequirementFailure.AiInvalidResponse)
        {
            Assert.NotNull(result.Attempt);
            Assert.Equal(DocumentProcessingOutcome.Failed, result.Attempt!.Outcome);
        }
    }

    [Fact]
    public async Task Execute_WhenRegisteredOperationIsCancelled_FinalizesCancelled()
    {
        var cancellation = new CancellationTokenSource();
        var context = CreateContext("success", File("source.pdf", PdfContentType));
        context.CancellationRegistry.Register(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(cancellation.Token);
        context.Ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<DocumentProcessingClientResult>(
                    cancellation.Token);
            });

        var result = await context.Service.ExecuteAsync(
            new ProcessRequirementCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProcessRequirementFailure.Cancelled, result.Failure);
        Assert.NotNull(result.Attempt);
        Assert.Equal(DocumentProcessingOutcome.Cancelled,
            result.Attempt!.Outcome);
        Assert.Equal(RequirementStatus.Pending, context.Requirement.Status);
        context.Requirements.DidNotReceive().AddExtractionResult(
            Arg.Any<RequirementExtractionResult>());
        context.Requirements.DidNotReceive().AddTechnicalProposal(
            Arg.Any<RequirementTechnicalProposal>());
    }

    private static Context CreateContext(
        string scenario,
        params RequirementFile[] files)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var storage = Substitute.For<IFileStorage>();
        var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
        var cancellationRegistry = Substitute.For<IOperationCancellationRegistry>();
        var user = User.CreateFromGoogle(
            "user@example.com", "User", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, UserId, At);
        var project = ProjectEntity.Create(
            client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2020-0001", null, At);
        var requirement = Requirement.Create(
            preQuote.Id,
            UserId,
            scenario.StartsWith("signature_", StringComparison.Ordinal)
                ? RequirementCommercialLine.Signature
                : RequirementCommercialLine.Essential,
            At.AddMinutes(1));
        RequirementProcessingAttempt? activeAttempt = null;

        if (scenario == "already_processing")
        {
            requirement.StartProcessing(At.AddMinutes(2));
        }
        else if (scenario == "processed")
        {
            requirement.StartProcessing(At.AddMinutes(2));
            requirement.MarkProcessed(At.AddMinutes(3));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindByIdAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(scenario == "not_found" ? null : requirement);
        requirements.ListFilesByRequirementIdAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(scenario == "no_files" ? [] : files);
        preQuotes.FindForUpdateByIdAsync(
                preQuote.Id,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);
        storage.OpenReadAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => scenario == "storage_error"
                ? Task.FromException<Stream>(new FileStorageReadException(
                    new IOException("sensitive")))
                : Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4])));
        cancellationRegistry.Register(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CancellationToken>(1));

        ai2.ProcessAsync(
                Arg.Any<DocumentProcessingClientRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => scenario switch
            {
                "ai_unavailable" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.ServiceUnavailable)),
                "ai_timeout" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.Timeout)),
                "ai_invalid" => Task.FromResult(
                    DocumentProcessingClientResult.Failed(
                        DocumentProcessingClientFailure.InvalidResponse)),
                "requires_review" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            DocumentProcessingOutcome.RequiresReview,
                            1))),
                "missing_glass" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            omitGlassSignal: true))),
                "missing_finish" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            omitFinishSignal: true))),
                "invalid_evidence_location" => Task.FromResult(
                    DocumentProcessingClientResult.Success(
                        CreateResponse(
                            call.Arg<DocumentProcessingClientRequest>(),
                            invalidEvidenceLocation: true))),
                _ => Task.FromResult(DocumentProcessingClientResult.Success(
                    CreateResponse(
                        call.Arg<DocumentProcessingClientRequest>(),
                        scenario: scenario)))
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        requirements.When(repository => repository.AddProcessingAttempt(
                Arg.Any<RequirementProcessingAttempt>()))
            .Do(call => activeAttempt =
                call.Arg<RequirementProcessingAttempt>());
        requirements.FinalizeProcessingFailureAsync(
                requirement.Id,
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var errorCode = call.ArgAt<string>(2);
                var completedAtUtc = call.ArgAt<DateTimeOffset>(3);
                if (activeAttempt is not null
                    && activeAttempt.ProcessingState
                        == DocumentProcessingState.Processing)
                {
                    activeAttempt.Fail(errorCode, completedAtUtc);
                }

                if (requirement.Status == RequirementStatus.Processing)
                {
                    requirement.MarkFailed(completedAtUtc);
                }

                preQuote.RegisterActivity(completedAtUtc);
                return new RequirementProcessingFailureFinalization(
                    requirement.Id,
                    call.ArgAt<Guid>(1),
                    activeAttempt?.CorrelationId ?? Guid.NewGuid(),
                    DocumentProcessingState.Finished,
                    DocumentProcessingOutcome.Failed,
                    errorCode,
                    activeAttempt?.StartedAtUtc ?? completedAtUtc,
                    completedAtUtc);
            });
        requirements.FinalizeProcessingCancellationAsync(
                requirement.Id,
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var completedAtUtc = call.ArgAt<DateTimeOffset>(2);
                if (activeAttempt is not null
                    && activeAttempt.ProcessingState
                        == DocumentProcessingState.Processing)
                {
                    activeAttempt.Cancel(completedAtUtc);
                }

                if (requirement.Status == RequirementStatus.Processing)
                {
                    requirement.MarkProcessingCancelled(completedAtUtc);
                }

                preQuote.RegisterActivity(completedAtUtc);
                return new RequirementProcessingCancellationFinalization(
                    requirement.Id,
                    call.ArgAt<Guid>(1),
                    activeAttempt?.CorrelationId ?? Guid.NewGuid(),
                    DocumentProcessingState.Finished,
                    DocumentProcessingOutcome.Cancelled,
                    activeAttempt?.StartedAtUtc ?? completedAtUtc,
                    completedAtUtc);
            });
        var productSystemItems = ProductSystemsFor(scenario);
        var productSystems = Substitute.For<IProductSystemCatalogRepository>();
        productSystems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns(call => scenario == "product_system_query_error"
                ? Task.FromException<IReadOnlyList<ProductSystemCatalogReadModel>>(
                    new InvalidOperationException(
                        "The LINQ expression could not be translated."))
                : Task.FromResult<IReadOnlyList<ProductSystemCatalogReadModel>>(
                    productSystemItems));
        var glassCatalog = Substitute.For<IGlassTypeCatalogRepository>();
        var glassCatalogItems = GlassCatalogItems();
        glassCatalog.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns(glassCatalogItems);
        var finishCatalog = Substitute.For<IFinishTypeCatalogRepository>();
        finishCatalog.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([Finish("BLACK_MATTE", "ALUCOLOR POLIESTER NEGRO MATE PP13")]);
        var similarity = Substitute.For<IHistoricalSimilarityEvaluationService>();
        similarity.EvaluateAsync(Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalSimilarityEvaluationResult(
                HistoricalSimilarityStatus.Completed,
                [],
                null));
        similarity.EvaluateBatchAsync(
                Arg.Any<IReadOnlyList<HistoricalSimilarityBatchQuery>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var queries = call.Arg<IReadOnlyList<HistoricalSimilarityBatchQuery>>();
                return Task.FromResult<IReadOnlyDictionary<string, HistoricalSimilarityEvaluationResult>>(
                    queries.ToDictionary(
                        value => value.RequestId,
                        _ => new HistoricalSimilarityEvaluationResult(
                            HistoricalSimilarityStatus.Completed,
                            [],
                            null),
                        StringComparer.Ordinal));
            });
        var technicalProposal = new BuildRequirementTechnicalProposalService(
            requirements,
            productSystems,
            glassCatalog,
            finishCatalog,
            new GlassCandidateResolver(),
            new FinishCandidateResolver(),
            new ResolveHistoricalTechnicalEvidenceService(
                similarity,
                productSystems,
                new DeterministicSgTechnicalSelector(productSystems)));

        var service = new ProcessRequirementService(
            new ProcessRequirementCommandValidator(),
            currentUser,
            identity,
            preQuotes,
            projects,
            clients,
            requirements,
            storage,
            ai2,
            cancellationRegistry,
            technicalProposal,
            new FixedTimeProvider(At.AddMinutes(10)),
            Substitute.For<ILogger<ProcessRequirementService>>());

        return new Context(
            service,
            requirement,
            project,
            requirements,
            ai2,
            cancellationRegistry,
            glassCatalogItems,
            productSystemItems);
    }

    private static ProductSystemCatalogReadModel ProductSystem(
        string code,
        string functionalType,
        string family,
        string commercialLine = "ESSENTIAL") =>
        new(
            Guid.NewGuid(),
            code,
            $"PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO {family}",
            $"PUERTA CORREDIZA SISTEMA VENECIA SERIE 70 {family}",
            family,
            functionalType,
            family,
            "SERIE 70",
            commercialLine,
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static IReadOnlyList<ProductSystemCatalogReadModel> ProductSystemsFor(
        string scenario)
    {
        if (scenario.StartsWith("assembly_", StringComparison.Ordinal))
        {
            return
            [
                ProductSystem("K70", "SLIDING_DOOR", "VENECIA NAPOLES"),
                ProductSystem("K50", "SLIDING_WINDOW", "VENECIA MONZA"),
                ProductSystem("S35", "PROJECTING", "PRIMAVERA SIENA", "CLASSIC"),
                ProductSystem("3890", "SWING_DOOR", "SG 3890", "CLASSIC"),
                ProductSystem("K40", "FIXED", "VENECIA FERMO"),
                ProductSystem("SG_LOUVER", "GRILLE", "LOUVER", "SPECIAL")
            ];
        }

        return scenario.StartsWith("signature_", StringComparison.Ordinal)
            ? [ProductSystem("SIG70", "SLIDING_DOOR", "SIGNATURE", "SIGNATURE")]
            : [ProductSystem("K70", "SLIDING_DOOR", "VENECIA NAPOLES")];
    }

    private static IReadOnlyList<GlassTypeCatalogReadModel> GlassCatalogItems() =>
    [
        Glass("TEMP_5", "COMPOSICION MONOLITICO TEMPLADO 5 MM INC"),
        Glass("TEMP_6", "COMPOSICION MONOLITICO TEMPLADO 6 MM INC"),
        Glass("TEMP_8", "COMPOSICION MONOLITICO TEMPLADO 8 MM INC"),
        Glass("TEMP_10", "COMPOSICION MONOLITICO TEMPLADO 10 MM INC"),
        Glass("LAM_4_4", "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC"),
        Glass("LAM_5_5", "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC")
    ];

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string name)
    {
        var thickness = code switch
        {
            "TEMP_5" => 5m,
            "TEMP_6" => 6m,
            "TEMP_8" => 8m,
            "TEMP_10" => 10m,
            "LAM_5_5" => 5m,
            _ => 4m
        };
        var laminated = code.StartsWith("LAM_", StringComparison.Ordinal);
        return
        new(
            Guid.NewGuid(),
            code,
            name,
            null,
            true,
            null,
            Family: laminated ? "LAMINATED" : "MONOLITHIC",
            Composition: laminated ? "RAW" : "TEMPERED",
            Treatment: laminated ? "RAW" : "TEMPERED",
            OuterThicknessMm: thickness,
            InnerThicknessMm: laminated ? thickness : null,
            PvbThicknessMm: laminated ? 0.38m : null,
            PvbColor: laminated ? "INC" : null,
            IsSelectable: true);
    }

    private static FinishTypeCatalogReadModel Finish(
        string code,
        string name) =>
        new(
            Guid.NewGuid(),
            code,
            name,
            "PAINTED",
            "BLACK",
            "MATTE",
            "PAINTED",
            null,
            "ALUMINUM",
            true,
            false,
            true);

    private static RequirementFile File(
        string fileName,
        string contentType)
    {
        return RequirementFile.Create(
            Guid.NewGuid(),
            fileName,
            contentType,
            4,
            $"requirements/{Guid.NewGuid():D}/{fileName}",
            At.AddMinutes(1));
    }

    private static IReadOnlyList<SourceEvidenceData> EvidenceForScenario(
        string scenario,
        string evidenceText) =>
        scenario.StartsWith("tempered_evidence_", StringComparison.Ordinal)
            ? [
                new SourceEvidenceData(
                    null,
                    EvidenceSourceType.Xlsx,
                    evidenceText,
                    "Cotizacion",
                    "A1:H1",
                    "source-1",
                    0.95m)
            ]
            : [];

    private static string SuggestedGlassCode(
        Context context,
        RequirementTechnicalProposalItem item) =>
        context.Glasses.Single(glass =>
            glass.GlassTypeId == item.SuggestedGlassTypeId).Code;

    private static string? SuggestedSystemCode(
        Context context,
        RequirementTechnicalProposalItem item) =>
        item.SuggestedSystemId is null
            ? null
            : context.Systems.Single(system =>
                system.Id == item.SuggestedSystemId).Code;

    private static StructuredItemSegmentData[] CreateSegments(
        IReadOnlyList<string> roles,
        IReadOnlyList<int> widths,
        IReadOnlyList<int> heights) =>
        CreateSegments(
            roles,
            widths.Select(value => (int?)value).ToArray(),
            heights.Select(value => (int?)value).ToArray());

    private static StructuredItemSegmentData[] CreateSegments(
        IReadOnlyList<string> roles,
        IReadOnlyList<int?> widths,
        IReadOnlyList<int> heights) =>
        CreateSegments(
            roles,
            widths,
            heights.Select(value => (int?)value).ToArray());

    private static StructuredItemSegmentData[] CreateSegments(
        IReadOnlyList<string> roles,
        IReadOnlyList<int?> widths,
        IReadOnlyList<int?> heights) =>
        roles.Select((role, index) => new StructuredItemSegmentData(
            index + 1,
            role,
            widths[index],
            heights[index],
            1,
            role,
            "RECTANGULAR",
            [
                new SourceEvidenceData(
                    null,
                    EvidenceSourceType.Xlsx,
                    $"Segmento {index + 1} {role}",
                    "Cotizacion",
                    $"A{index + 1}:B{index + 1}")
            ])).ToArray();

    private static DocumentProcessingResponseData CreateResponse(
        DocumentProcessingClientRequest request,
        DocumentProcessingOutcome outcome = DocumentProcessingOutcome.Completed,
        int itemsRequiringReview = 0,
        bool invalidEvidenceLocation = false,
        bool omitGlassSignal = false,
        bool omitFinishSignal = false,
        string scenario = "success")
    {
        var width = scenario switch
        {
            "tempered_2400" => 1200,
            "tempered_2401" => 1200,
            "tempered_2601" => 1200,
            "tempered_2801" => 1200,
            "tempered_single_2780" => 1200,
            "tempered_horizontal_split_2780" => 1200,
            "tempered_joint_1951" => 1951,
            "tempered_joint_5000" => 5000,
            "tempered_explicit_two_panes_3300" => 3300,
            "tempered_two_panel_no_distribution_3300" => 3300,
            "tempered_explicit_joint_pane_2050" => 2050,
            "tempered_explicit_two_panes_3800" => 3800,
            "tempered_heterogeneous_panes" => 2200,
            "tempered_evidence_vertical_uniform_900_2700" => 900,
            "tempered_evidence_vertical_nonuniform_1000_2700" => 1000,
            "tempered_evidence_horizontal_4000" => 4000,
            "tempered_evidence_horizontal_joint_4500" => 4500,
            "tempered_evidence_inconsistent_900_2700" => 900,
            "tempered_evidence_ambiguous_4000" => 4000,
            "skylight_roof_geometry_5110_920" => 920,
            "skylight_missing_length_920_2700" => 920,
            "skylight_roof_panelcount_6_total" => 3000,
            "skylight_roof_segments_1000_1500" => 5000,
            "pocket_leaf_2000_total_4000" => 4000,
            "pocket_leaf_1500_total_3000" => 3000,
            "pocket_two_leaves_1200_total_4800" => 4800,
            "pocket_overall_4000_panel_1" => 4000,
            "pocket_two_cuerpos_4800" => 4800,
            "sliding_non_pocket_3000_2700" => 3000,
            "tempered_three_panel_4000" => 4000,
            "tempered_two_panel_3000" => 3000,
            "tempered_three_panel_5000" => 5000,
            "tempered_single_pane_3000" => 3000,
            "tempered_single_pane_1200_2700" => 1200,
            "tempered_pane_1950" => 1950,
            "tempered_pane_1951" => 1951,
            "tempered_explicit_widths_4000" => 4000,
            "tempered_explicit_uniform_widths_3000" => 3000,
            "tempered_explicit_narrow_width_500" => 1000,
            "tempered_segments_4100" => 4100,
            "tempered_unknown_3000" => 3000,
            "signature_laminated_5_5" => 5000,
            "signature_laminated_three_panel_5000" => 5000,
            "signature_laminated_explicit_widths_5000" => 5000,
            _ => 3740
        };
        var height = scenario switch
        {
            "assembly_sliding_window_lower_fixed" => 2700,
            "assembly_sliding_window_mobile_over_threshold" => 3000,
            "assembly_sliding_window_unresolved_geometry" => 2700,
            "tempered_explicit_two_panes_3300" => 2400,
            "tempered_two_panel_no_distribution_3300" => 2400,
            "tempered_2400" => 2400,
            "tempered_2401" => 2401,
            "tempered_2601" => 2601,
            "tempered_2801" => 2801,
            "tempered_narrow_2700" => 2700,
            "tempered_single_2780" => 2780,
            "tempered_horizontal_split_2780" => 2780,
            "tempered_explicit_narrow_width_500" => 2700,
            "tempered_explicit_joint_pane_2050" => 2700,
            "tempered_explicit_two_panes_3800" => 2700,
            "tempered_heterogeneous_panes" => 2700,
            "tempered_evidence_vertical_uniform_900_2700" => 2700,
            "tempered_evidence_vertical_nonuniform_1000_2700" => 2700,
            "tempered_evidence_horizontal_4000" => 2400,
            "tempered_evidence_horizontal_joint_4500" => 2400,
            "tempered_evidence_inconsistent_900_2700" => 2700,
            "tempered_evidence_ambiguous_4000" => 2400,
            "skylight_roof_geometry_5110_920" => 2700,
            "skylight_missing_length_920_2700" => 2700,
            "skylight_roof_panelcount_6_total" => 2700,
            "skylight_roof_segments_1000_1500" => 2700,
            "pocket_leaf_2000_total_4000" => 2700,
            "pocket_leaf_1500_total_3000" => 2700,
            "pocket_two_leaves_1200_total_4800" => 2700,
            "pocket_overall_4000_panel_1" => 2700,
            "pocket_two_cuerpos_4800" => 2700,
            "sliding_non_pocket_3000_2700" => 2700,
            "tempered_single_pane_1200_2700" => 2700,
            "tempered_segments_4100" => 2800,
            "signature_laminated_5_5" => 3000,
            "signature_laminated_three_panel_5000" => 3000,
            "signature_laminated_explicit_widths_5000" => 3000,
            _ => 2500
        };
        if (scenario == "tempered_narrow_2700")
        {
            width = 500;
        }

        var elementType = scenario switch
        {
            "special_shower" => StructuredElementType.ShowerDivision,
            "special_railing" => StructuredElementType.Railing,
            "assembly_projecting_fixed" => StructuredElementType.Window,
            "assembly_sliding_window_grille" => StructuredElementType.Window,
            "assembly_sliding_window_lower_fixed" => StructuredElementType.Window,
            "assembly_sliding_window_mobile_over_threshold" => StructuredElementType.Window,
            "assembly_sliding_window_unresolved_geometry" => StructuredElementType.Window,
            "assembly_fixed_grille" => StructuredElementType.Window,
            "assembly_grille_only" => StructuredElementType.Other,
            "assembly_shower_sliding_fixed" => StructuredElementType.ShowerDivision,
            "assembly_skylight_fixed" => StructuredElementType.Skylight,
            "skylight_roof_geometry_5110_920" => StructuredElementType.Skylight,
            "skylight_missing_length_920_2700" => StructuredElementType.Skylight,
            "skylight_roof_panelcount_6_total" => StructuredElementType.Skylight,
            "skylight_roof_segments_1000_1500" => StructuredElementType.Skylight,
            _ => StructuredElementType.Door
        };
        var functionalType = scenario switch
        {
            "special_shower" => "SHOWER_DIVISION",
            "special_railing" => "RAILING",
            "assembly_sliding_window_grille" => "SLIDING_WINDOW",
            "assembly_sliding_window_lower_fixed" => "SLIDING_WINDOW",
            "assembly_sliding_window_mobile_over_threshold" => "SLIDING_WINDOW",
            "assembly_sliding_window_unresolved_geometry" => "SLIDING_WINDOW",
            "assembly_sliding_door_fixed" => "SLIDING_DOOR",
            "assembly_shower_sliding_fixed" => "SHOWER_DIVISION",
            "assembly_skylight_fixed" => "SKYLIGHT",
            "skylight_roof_geometry_5110_920" => "SKYLIGHT",
            "skylight_missing_length_920_2700" => "SKYLIGHT",
            "skylight_roof_panelcount_6_total" => "SKYLIGHT",
            "skylight_roof_segments_1000_1500" => "SKYLIGHT",
            "pocket_leaf_2000_total_4000" => "SLIDING_DOOR",
            "pocket_leaf_1500_total_3000" => "SLIDING_DOOR",
            "pocket_two_leaves_1200_total_4800" => "SLIDING_DOOR",
            "pocket_overall_4000_panel_1" => "SLIDING_DOOR",
            "pocket_two_cuerpos_4800" => "SLIDING_DOOR",
            "sliding_non_pocket_3000_2700" => "SLIDING_DOOR",
            "assembly_projecting_fixed" => "PROJECTING",
            "assembly_swing_fixed" => "SWING_DOOR",
            _ when scenario.StartsWith("assembly_", StringComparison.Ordinal) =>
                "Blocks_Ventana descriptive raw text",
            _ => "SLIDING_DOOR"
        };
        int? panelCount = scenario switch
        {
            "tempered_joint_1951" => 1,
            "tempered_joint_5000" => 1,
            "tempered_single_pane_3000" => 1,
            "tempered_pane_1950" => 1,
            "tempered_pane_1951" => 1,
            "tempered_explicit_joint_pane_2050" => 1,
            "tempered_single_pane_1200_2700" => 1,
            "tempered_single_2780" => 1,
            "signature_laminated_5_5" => 1,
            "tempered_three_panel_4000" => 3,
            "tempered_two_panel_3000" => 2,
            "tempered_three_panel_5000" => 3,
            "tempered_explicit_two_panes_3300" => 2,
            "tempered_two_panel_no_distribution_3300" => 2,
            "tempered_explicit_two_panes_3800" => 2,
            "tempered_heterogeneous_panes" => 2,
            "tempered_evidence_vertical_uniform_900_2700" => 3,
            "tempered_evidence_vertical_nonuniform_1000_2700" => 3,
            "tempered_evidence_horizontal_4000" => 3,
            "tempered_evidence_horizontal_joint_4500" => 3,
            "tempered_evidence_inconsistent_900_2700" => 3,
            "tempered_evidence_ambiguous_4000" => 3,
            "skylight_roof_panelcount_6_total" => 6,
            "skylight_roof_segments_1000_1500" => 6,
            "pocket_leaf_2000_total_4000" => 1,
            "pocket_leaf_1500_total_3000" => 1,
            "pocket_two_leaves_1200_total_4800" => 2,
            "pocket_overall_4000_panel_1" => 1,
            "pocket_two_cuerpos_4800" => 2,
            "sliding_non_pocket_3000_2700" => 1,
            "signature_laminated_three_panel_5000" => 3,
            "tempered_explicit_widths_4000" => 3,
            "tempered_explicit_uniform_widths_3000" => 3,
            "tempered_explicit_narrow_width_500" => 2,
            "tempered_segments_4100" => 2,
            "signature_laminated_explicit_widths_5000" => 3,
            "tempered_unknown_3000" => null,
            "tempered_horizontal_split_2780" => null,
            _ => 1
        };
        var modulation = scenario switch
        {
            "tempered_horizontal_split_2780" =>
                "HORIZONTAL_HEIGHTS_700_2080",
            "tempered_explicit_widths_4000" =>
                "VERTICAL_WIDTHS_1000_2000_1000",
            "tempered_explicit_uniform_widths_3000" =>
                "VERTICAL_WIDTHS_1000_1000_1000",
            "tempered_explicit_narrow_width_500" =>
                "VERTICAL_WIDTHS_500_500",
            "tempered_explicit_two_panes_3300" =>
                "VERTICAL_WIDTHS_1650_1650",
            "tempered_two_panel_no_distribution_3300" =>
                "TWO_PANELS",
            "skylight_roof_panelcount_6_total" =>
                "ROOF_TOTAL",
            "pocket_overall_4000_panel_1" =>
                "POCKET_TOTAL_SPAN",
            "pocket_two_cuerpos_4800" =>
                "2 cuerpos pocket",
            "tempered_evidence_ambiguous_4000" =>
                "3 cuerpos",
            "tempered_explicit_two_panes_3800" =>
                "VERTICAL_WIDTHS_1900_1900",
            "signature_laminated_explicit_widths_5000" =>
                "VERTICAL_WIDTHS_2000_1500_1500",
            _ => "TWO_PANELS"
        };
        var segmentRoles = scenario switch
        {
            "assembly_sliding_fixed_grille" => ["SLIDING", "FIXED", "GRILLE"],
            "assembly_projecting_fixed" => ["PROJECTING", "FIXED"],
            "assembly_swing_fixed" => ["SWING", "FIXED"],
            "assembly_fixed_grille" => ["FIXED", "GRILLE"],
            "assembly_grille_only" => ["GRILLE"],
            "assembly_sliding_swing" => ["SLIDING", "SWING"],
            "assembly_sliding_window_grille" => ["SLIDING", "GRILLE"],
            "assembly_sliding_window_lower_fixed" => ["SLIDING", "FIXED"],
            "assembly_sliding_window_mobile_over_threshold" => ["SLIDING", "FIXED"],
            "assembly_sliding_window_unresolved_geometry" => ["SLIDING", "FIXED"],
            "assembly_sliding_door_fixed" => ["SLIDING", "FIXED"],
            "assembly_shower_sliding_fixed" => ["SLIDING", "FIXED"],
            "assembly_skylight_fixed" => ["FIXED"],
            _ => Array.Empty<string>()
        };
        var segments = scenario == "tempered_segments_4100"
            ? CreateSegments(["FIXED", "FIXED"], [2050, 2050], [2800, 2800])
            : scenario == "assembly_sliding_window_lower_fixed"
                ? CreateSegments(["SLIDING", "FIXED"], [1800, 1800], [1800, 900])
            : scenario == "assembly_sliding_window_mobile_over_threshold"
                ? CreateSegments(["SLIDING", "FIXED"], [1800, 1800], [2800, 200])
            : scenario == "assembly_sliding_window_unresolved_geometry"
                ? CreateSegments(
                    ["SLIDING", "FIXED"],
                    [null, null],
                    [null, null])
            : scenario == "tempered_heterogeneous_panes"
                ? CreateSegments(["FIXED", "FIXED"], [500, 1700], [2700, 2700])
            : scenario == "tempered_evidence_vertical_nonuniform_1000_2700"
                ? CreateSegments(["FIXED", "FIXED", "FIXED"], [1000, 1000, 1000], [700, 1100, 900])
            : scenario == "skylight_roof_segments_1000_1500"
                ? CreateSegments(
                    ["FIXED", "FIXED", "FIXED"],
                    [1000, 1000, 1000],
                    [1500, 1500, 1500])
            : segmentRoles.Length > 0
                ? CreateSegments(
                    segmentRoles,
                    Enumerable.Repeat(1000, segmentRoles.Length).ToArray(),
                    Enumerable.Repeat(1500, segmentRoles.Length).ToArray())
                : null;
        var status = outcome == DocumentProcessingOutcome.RequiresReview
            ? StructuredExtractionStatus.RequiresReview
            : StructuredExtractionStatus.Completed;
        var glassRaw = scenario == "tempered_segments_4100"
            ? "templado 10 mm"
            : "templado 6 mm";
        var glassThickness = scenario == "tempered_segments_4100" ? 10m : 6m;
        var evidenceText = scenario switch
        {
            "tempered_evidence_vertical_uniform_900_2700" =>
                "altura de 2.70m dividida en 3 tramos verticales de 0.90m cada uno",
            "tempered_evidence_horizontal_4000" =>
                "dividida horizontalmente en paÃƒÂ±os de 1500 mm, 1000 mm y 1500 mm",
            "tempered_evidence_horizontal_joint_4500" =>
                "dividida horizontalmente en paÃƒÂ±os de 1000 mm, 2500 mm y 1000 mm",
            "tempered_evidence_inconsistent_900_2700" =>
                "tramo superior 900 mm, central 900 mm, inferior 500 mm",
            "skylight_roof_geometry_5110_920" =>
                "Techo en vidrio Largo = 5.11 m Ancho = 0.92 m Altura = 2.70 m",
            "skylight_missing_length_920_2700" =>
                "Techo en vidrio Ancho = 0.92 m Altura = 2.70 m",
            "skylight_roof_panelcount_6_total" =>
                "Cubierta en vidrio Largo = 5.00 m Ancho = 3.00 m Altura = 2.70 m",
            "skylight_roof_segments_1000_1500" =>
                "Cubierta en vidrio Largo = 5.00 m Ancho = 3.00 m Altura = 2.70 m modulos 1000 x 1500 mm",
            "pocket_leaf_2000_total_4000" =>
                "Corrediza tipo pocket hoja width = 2000 mm span total = 4000 mm bolsillo = 2000 mm",
            "pocket_leaf_1500_total_3000" =>
                "Puerta bolsillo ancho de hoja = 1500 mm overall width = 3000 mm",
            "pocket_two_leaves_1200_total_4800" =>
                "Corrediza tipo bolsillo 2 hojas de 1200 mm span total = 4800 mm",
            "pocket_overall_4000_panel_1" =>
                "Corrediza tipo pocket span total = 4000 mm bolsillo incluido",
            "pocket_two_cuerpos_4800" =>
                "2 cuerpos pocket overall width = 4800 mm",
            _ => "PV-06 Puerta vidriera"
        };
        var description = scenario switch
        {
            "skylight_roof_geometry_5110_920" => "Techo en vidrio",
            "skylight_missing_length_920_2700" => "Techo en vidrio",
            "skylight_roof_panelcount_6_total" => "Cubierta en vidrio",
            "skylight_roof_segments_1000_1500" => "Cubierta en vidrio",
            "pocket_leaf_2000_total_4000" =>
                "Puerta Vidriera Corrediza 1 Cuerpo Bolsillo",
            "pocket_leaf_1500_total_3000" =>
                "Puerta Vidriera Corrediza Tipo Pocket",
            "pocket_two_leaves_1200_total_4800" =>
                "Puerta Vidriera Corrediza Tipo Bolsillo",
            "pocket_overall_4000_panel_1" =>
                "Puerta Vidriera Corrediza Pocket",
            "pocket_two_cuerpos_4800" =>
                "Puerta Vidriera Corrediza Bolsillo",
            _ => "Puerta vidriera"
        };
        var item = new StructuredItemData(
            1,
            "PV-06",
            description,
            elementType,
            $"{width} x {height}",
            width,
            height,
            1,
            itemsRequiringReview > 0,
            [],
            [],
            EvidenceForScenario(scenario, evidenceText),
            new StructuredItemGlassData(
                omitGlassSignal ? null : glassRaw,
                omitGlassSignal ? null : "templado",
                GlassAssignmentScope.Item,
                false,
                [],
                [],
                [
                    new SourceEvidenceData(
                        invalidEvidenceLocation ? 0 : null,
                        invalidEvidenceLocation
                            ? EvidenceSourceType.Native
                            : EvidenceSourceType.Xlsx,
                        evidenceText,
                        invalidEvidenceLocation ? null : "Cotizacion",
                        invalidEvidenceLocation ? null : "A12:H12",
                        "source-1",
                        0.95m)
                ]),
            null,
            9.35m,
            "corrediza",
            0.92m,
            CanonicalExtractionValueStatus.Explicit,
            functionalType,
            "SLIDING",
            panelCount,
            null,
            null,
            modulation,
            null,
            [],
            null,
            "element-pv06",
            "TWO_PANELS",
            "3831",
            "3831",
            omitGlassSignal ? null : "templado",
            omitGlassSignal ? null : "templado",
            omitGlassSignal ? null : glassThickness,
            null,
            null,
            null,
            null,
            omitGlassSignal ? null : "monolitico",
            null,
            null,
            omitFinishSignal ? null : "negro pintura al horno",
            omitFinishSignal ? null : "PAINTED",
            null,
            omitFinishSignal ? null : "BLACK",
            null,
            omitFinishSignal ? null : "MATTE",
            null,
            false,
            segments is null ? null : "MULTI_MODULE",
            segments);
        var structuredExtraction = new StructuredExtractionData(
            status,
            "Proyecto",
            "Cliente",
            "Bogota",
            [],
            [],
            [],
            [item],
            [],
            [],
            [],
            15,
            0,
            itemsRequiringReview,
            15,
            "ai2_requirement_extraction",
            1234,
            1,
            itemsRequiringReview);

        return new DocumentProcessingResponseData(
            "AI2-1.0",
            request.DocumentId,
            request.ProcessingAttemptId,
            outcome,
            new ProcessedDocumentData(
                request.Files[0].FileName,
                request.Files[0].ContentType,
                request.Files.Sum(file => file.SizeBytes),
                0,
                DocumentClassification.Xlsx,
                false),
            [],
            [],
            new ProcessingMetadataData("ai2", 1234),
            "{\"requirement\":{},\"elements\":[]}",
            structuredExtraction,
            DocumentProcessingProvider.Ai2);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record Context(
        ProcessRequirementService Service,
        Requirement Requirement,
        ProjectEntity Project,
        IRequirementRepository Requirements,
        IAi2DocumentProcessingClient Ai2,
        IOperationCancellationRegistry CancellationRegistry,
        IReadOnlyList<GlassTypeCatalogReadModel> Glasses,
        IReadOnlyList<ProductSystemCatalogReadModel> Systems);
}
