using System.Reflection;
using Application.PreQuotes.TechnicalProposalReadiness;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class TechnicalProposalReadinessEvaluatorTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SystemId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GlassId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FinishId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void EvaluateItem_WithSystemNull_ReturnsBlockingSystemDefinition()
    {
        var item = ProposalItem(
            withoutSystem: true,
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons: ["SYSTEM_NOT_RESOLVED"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("BLOCKED", readiness.State);
        Assert.Equal("REVIEW_SYSTEM", definition.Code);
        Assert.Equal("SYSTEM", definition.Category);
        Assert.Equal("BLOCKING", definition.Severity);
        Assert.Equal("system", definition.Field);
        Assert.Equal("Revisar sistema", definition.Title);
        Assert.True(definition.BlocksPricing);
        Assert.Contains("sistema", definition.RequiredAction);
        Assert.Contains("SYSTEM_NOT_RESOLVED", definition.RelatedReasonCodes);
    }

    [Fact]
    public void EvaluateItem_WithTechnicalAmbiguity_ReturnsHumanAction()
    {
        var item = ProposalItem(
            requiresReview: true,
            reviewReasons: ["TECHNICAL_SELECTION_AMBIGUOUS"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_SYSTEM_WARNING", definition.Code);
        Assert.Equal("SYSTEM", definition.Category);
        Assert.Equal("Advertencia de sistema", definition.Title);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
        Assert.Contains("TECHNICAL_SELECTION_AMBIGUOUS", definition.RelatedReasonCodes);
    }

    [Fact]
    public void EvaluateItem_WithGlassPaneGeometryUnresolvedAndNotPriceable_DoesNotBlockPricingWhenConfigurationIsComplete()
    {
        var item = ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons: ["GLASS_PANE_GEOMETRY_UNRESOLVED"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_GLASS_WARNING", definition.Code);
        Assert.Equal("GLASS", definition.Category);
        Assert.Equal("glass", definition.Field);
        Assert.Equal("Advertencia de vidrio", definition.Title);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithGlassPaneGeometryUnresolvedAndPriceable_IsReviewOnly()
    {
        var item = ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: true,
            isPriceable: true,
            reviewReasons: ["GLASS_PANE_GEOMETRY_UNRESOLVED"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_GLASS_WARNING", definition.Code);
        Assert.Equal("Advertencia de vidrio", definition.Title);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithSimilarityUnavailableOnly_DoesNotBlockPricing()
    {
        var item = ProposalItem(
            requiresReview: true,
            reviewReasons: ["SIMILARITY_UNAVAILABLE"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("INFO", definition.Severity);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithInvalidEvidenceOnly_ReturnsWarning()
    {
        var item = ProposalItem(
            requiresReview: true,
            reviewReasons: ["INVALID_EVIDENCE_LOCATION"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_EVIDENCE", definition.Code);
        Assert.Equal("Revisar evidencia", definition.Title);
        Assert.Equal("EVIDENCE", definition.Category);
        Assert.Equal("WARNING", definition.Severity);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithRequiresReviewAndNoReasons_ReturnsReady()
    {
        var item = ProposalItem(requiresReview: true);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);

        Assert.Equal("READY", readiness.State);
        Assert.Equal(0, readiness.BlockingCount);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Empty(readiness.PendingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithRequiresReviewFalseAndNoReasons_ReturnsReady()
    {
        var item = ProposalItem(requiresReview: false);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);

        Assert.Equal("READY", readiness.State);
        Assert.Equal(0, readiness.BlockingCount);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Empty(readiness.PendingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithUnknownReviewReason_ReturnsUnclassifiedReview()
    {
        var item = ProposalItem(
            requiresReview: true,
            reviewReasons: ["SOME_NEW_UNKNOWN_REASON"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_REQUIRED_UNCLASSIFIED", definition.Code);
        Assert.Equal("OTHER", definition.Category);
        Assert.Equal("Revisar configuracion", definition.Title);
        Assert.Contains("SOME_NEW_UNKNOWN_REASON", definition.RelatedReasonCodes);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithFullyConfiguredItem_ReturnsReady()
    {
        var item = ProposalItem();

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);

        Assert.Equal("READY", readiness.State);
        Assert.Equal(0, readiness.BlockingCount);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Empty(readiness.PendingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithTraceOnlyResolutionReasons_ReturnsReady()
    {
        var item = ProposalItem(
            systemReasons:
            [
                "SYSTEM_FIXED_FERMO",
                "SYSTEM_PROJECTING_SIENA",
                "SYSTEM_HISTORICAL_SUPPORT"
            ],
            glassReasons:
            [
                "GLASS_LINE_TEMPERED",
                "GLASS_PANE_DIMENSIONS_FROM_ELEMENT",
                "JOINT_GLASS_RULE"
            ],
            finishReasons: ["HISTORICAL_DEFAULT_FINISH"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);

        Assert.Equal("READY", readiness.State);
        Assert.Equal(0, readiness.BlockingCount);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Empty(readiness.PendingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithSystemReasonsGroupsRelatedCodes()
    {
        var item = ProposalItem(
            withoutSystem: true,
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons:
            [
                "SYSTEM_NOT_RESOLVED",
                "RULE_NOT_DEFINED_REQUIRES_REVIEW"
            ]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_SYSTEM", definition.Code);
        Assert.Equal("Revisar sistema", definition.Title);
        Assert.Contains("SYSTEM_NOT_RESOLVED", definition.RelatedReasonCodes);
        Assert.DoesNotContain("RULE_NOT_DEFINED_REQUIRES_REVIEW", definition.RelatedReasonCodes);
    }

    [Fact]
    public void EvaluateItem_WithRuleNotDefinedAndCompleteSuggested_IsReviewOnly()
    {
        var item = ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: true,
            isPriceable: true,
            reviewReasons: ["RULE_NOT_DEFINED_REQUIRES_REVIEW"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_SYSTEM_WARNING", definition.Code);
        Assert.False(definition.BlocksConfirmation);
        Assert.False(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithInvalidQuantity_BlocksPricing()
    {
        var item = ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            quantity: null);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("BLOCKED", readiness.State);
        Assert.Equal("REVIEW_QUANTITY", definition.Code);
        Assert.True(definition.BlocksConfirmation);
        Assert.True(definition.BlocksPricing);
    }

    [Fact]
    public void EvaluateItem_WithGlassPaneReasonsGroupsRelatedCodes()
    {
        var item = ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons:
            [
                "GLASS_PANE_GEOMETRY_UNRESOLVED",
                "GLASS_PANE_HETEROGENEOUS_NEEDS"
            ]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_GLASS_WARNING", definition.Code);
        Assert.Equal("Advertencia de vidrio", definition.Title);
        Assert.Contains("GLASS_PANE_GEOMETRY_UNRESOLVED", definition.RelatedReasonCodes);
        Assert.Contains("GLASS_PANE_HETEROGENEOUS_NEEDS", definition.RelatedReasonCodes);
    }

    [Fact]
    public void EvaluateItem_WithTraceReasonsAndInvalidEvidence_ReturnsOnlyEvidenceWarning()
    {
        var item = ProposalItem(
            requiresReview: true,
            reviewReasons: ["INVALID_EVIDENCE_LOCATION"],
            systemReasons: ["SYSTEM_PROJECTING_SIENA"],
            glassReasons: ["GLASS_LINE_TEMPERED"],
            finishReasons: ["HISTORICAL_DEFAULT_FINISH"]);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.Equal("REVIEW_EVIDENCE", definition.Code);
        Assert.Equal("Revisar evidencia", definition.Title);
        Assert.Equal("WARNING", definition.Severity);
    }

    [Fact]
    public void EvaluateProposal_WithMixedState_ReturnsCountsByStateAndCategory()
    {
        var system = TechnicalProposalReadinessEvaluator.EvaluateItem(ProposalItem(
            withoutSystem: true,
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons: ["SYSTEM_NOT_RESOLVED"]));
        var evidence = TechnicalProposalReadinessEvaluator.EvaluateItem(ProposalItem(
            requiresReview: true,
            reviewReasons: ["INVALID_EVIDENCE_LOCATION"]));
        var legacyRequiresReview = TechnicalProposalReadinessEvaluator.EvaluateItem(
            ProposalItem(requiresReview: true));
        var ready = TechnicalProposalReadinessEvaluator.EvaluateItem(ProposalItem());

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateProposal(
            [system, evidence, legacyRequiresReview, ready]);

        Assert.Equal("BLOCKED", readiness.State);
        Assert.False(readiness.IsReadyForConfirmation);
        Assert.False(readiness.IsReadyForPricing);
        Assert.Equal(1, readiness.BlockingItems);
        Assert.Equal(1, readiness.WarningItems);
        Assert.Equal(1, readiness.BlockingDefinitions);
        Assert.Equal(1, readiness.WarningDefinitions);
        Assert.Equal(1, readiness.PricingBlockingItems);
        Assert.Equal(1, readiness.PricingBlockingDefinitions);
        Assert.Equal(1, readiness.Categories["SYSTEM"]);
        Assert.Equal(1, readiness.Categories["EVIDENCE"]);
        Assert.False(readiness.Categories.ContainsKey("OTHER"));
    }

    [Fact]
    public void EvaluateProposal_WithConfirmationOnlyReview_DoesNotBlockPricing()
    {
        var review = TechnicalProposalReadinessEvaluator.EvaluateItem(ProposalItem(
            requiresReview: true,
            isTechnicallyComplete: true,
            isPriceable: true,
            reviewReasons: ["GLASS_PANE_GEOMETRY_UNRESOLVED"]));
        var ready = TechnicalProposalReadinessEvaluator.EvaluateItem(ProposalItem());

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateProposal(
            [review, ready]);

        Assert.Equal("REVIEW_REQUIRED", readiness.State);
        Assert.True(readiness.IsReadyForConfirmation);
        Assert.True(readiness.IsReadyForPricing);
        Assert.Equal(0, readiness.BlockingItems);
        Assert.Equal(0, readiness.BlockingDefinitions);
        Assert.Equal(0, readiness.PricingBlockingItems);
        Assert.Equal(0, readiness.PricingBlockingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithStaleSystemNotResolvedAndSelectedConfiguration_ReturnsReady()
    {
        var item = ProposalItem(
            withoutSystem: true,
            requiresReview: true,
            isTechnicallyComplete: false,
            isPriceable: false,
            reviewReasons: ["SYSTEM_NOT_RESOLVED"],
            selectCompleteConfiguration: true);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);

        Assert.Equal("READY", readiness.State);
        Assert.Empty(readiness.PendingDefinitions);
    }

    [Fact]
    public void EvaluateItem_WithMissingMeasurements_BlocksPricing()
    {
        var item = ProposalItem(widthMillimeters: null);

        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var definition = Assert.Single(readiness.PendingDefinitions);

        Assert.Equal("BLOCKED", readiness.State);
        Assert.Equal("REVIEW_MEASUREMENTS", definition.Code);
        Assert.True(definition.BlocksConfirmation);
        Assert.True(definition.BlocksPricing);
        Assert.Contains("MissingOrInvalidMeasurements", definition.RelatedReasonCodes);
    }

    private static RequirementTechnicalProposalItem ProposalItem(
        bool withoutSystem = false,
        bool withoutGlass = false,
        bool withoutFinish = false,
        bool requiresReview = false,
        bool isTechnicallyComplete = true,
        bool isPriceable = true,
        IReadOnlyList<string>? reviewReasons = null,
        IReadOnlyList<string>? systemReasons = null,
        IReadOnlyList<string>? glassReasons = null,
        IReadOnlyList<string>? finishReasons = null,
        int? quantity = 1,
        int? widthMillimeters = 1000,
        int? heightMillimeters = 1000,
        bool selectCompleteConfiguration = false)
    {
        var extracted = RequirementExtractedItem.Create(
            Guid.NewGuid(),
            "element-1",
            1,
            "V-01",
            "Ventana",
            StructuredElementType.Window,
            quantity,
            widthMillimeters,
            heightMillimeters,
            1m,
            0.90m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "FIXED",
            "FIXED",
            null,
            null,
            null,
            null,
            null,
            null,
            [],
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
            "MONOLITICO",
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
        var item = RequirementTechnicalProposalItem.Create(
            Guid.NewGuid(),
            extracted.Id,
            withoutSystem ? null : SystemId,
            withoutGlass ? null : GlassId,
            withoutFinish ? null : FinishId,
            0.90m,
            0.90m,
            0.90m,
            0.90m,
            requiresReview,
            isTechnicallyComplete,
            isPriceable,
            reviewReasons ?? [],
            systemReasons ?? [],
            glassReasons ?? [],
            finishReasons ?? [],
            0,
            null,
            null,
            "NotEvaluated",
            At);
        SetPrivateProperty(item, "ExtractedItem", extracted);
        if (selectCompleteConfiguration)
        {
            item.Select(
                SystemId,
                GlassId,
                FinishId,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                At.AddMinutes(1));
        }

        return item;
    }

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
}
