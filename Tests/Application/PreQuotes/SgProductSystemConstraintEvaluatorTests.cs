using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class SgProductSystemConstraintEvaluatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_PreSelectionMaxWidth_FailsWhenOpeningIsTooWide()
    {
        var result = Evaluator().Evaluate(
            System(Constraint(
                "MAX_OPENING_WIDTH",
                ProductSystemConstraintType.MaxWidth,
                maxValue: 3000m,
                severity: ProductSystemConstraintSeverity.Hard,
                knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)),
            Input(width: 3200),
            ConstraintEvaluationStage.PreSelection);

        Assert.True(result.HasHardFailure);
        Assert.Contains(result.Evaluations, value =>
            value.State == ProductSystemConstraintEvaluationState.Fail
            && value.ConstraintCode == "MAX_OPENING_WIDTH");
    }

    [Fact]
    public void Evaluate_PreSelectionMaxHeight_PassesWhenOpeningIsInsideLimit()
    {
        var result = Evaluator().Evaluate(
            System(Constraint(
                "MAX_OPENING_HEIGHT",
                ProductSystemConstraintType.MaxHeight,
                maxValue: 2600m)),
            Input(height: 2500),
            ConstraintEvaluationStage.PreSelection);

        Assert.False(result.HasHardFailure);
        Assert.Contains(result.Evaluations, value =>
            value.State == ProductSystemConstraintEvaluationState.Pass);
    }

    [Fact]
    public void Evaluate_MissingRequiredMeasurement_IsUnknownAndCanRequireReview()
    {
        var result = Evaluator().Evaluate(
            System(Constraint(
                "MAX_OPENING_WIDTH",
                ProductSystemConstraintType.MaxWidth,
                maxValue: 3000m,
                requiresReviewWhenUnknown: true)),
            Input(width: null),
            ConstraintEvaluationStage.PreSelection);

        Assert.True(result.HasUnknownReview);
        Assert.Contains("SYSTEM_CONSTRAINT_MAX_OPENING_WIDTH_UNKNOWN",
            result.ReviewReasons);
    }

    [Fact]
    public void Evaluate_AreaAndPanelCount_UseDirectInputValues()
    {
        var result = Evaluator().Evaluate(
            System(
                Constraint("MIN_AREA", ProductSystemConstraintType.MinArea,
                    minValue: 2m),
                Constraint("MAX_PANELS", ProductSystemConstraintType.MaxPanelCount,
                    maxValue: 3m)),
            Input(area: 2.5m, panelCount: 4),
            ConstraintEvaluationStage.PreSelection);

        Assert.Contains(result.Evaluations, value =>
            value.ConstraintCode == "MIN_AREA"
            && value.State == ProductSystemConstraintEvaluationState.Pass);
        Assert.Contains(result.Evaluations, value =>
            value.ConstraintCode == "MAX_PANELS"
            && value.State == ProductSystemConstraintEvaluationState.Fail);
    }

    [Fact]
    public void Evaluate_OperationGeometryAndFeatures_AreMatchedByCodes()
    {
        var result = Evaluator().Evaluate(
            System(
                Constraint("ALLOWED_OPERATION", ProductSystemConstraintType.AllowedOperation,
                    allowedValues: ["SLIDING"]),
                Constraint("FORBIDDEN_GEOMETRY", ProductSystemConstraintType.ForbiddenGeometry,
                    allowedValues: ["ARCH"]),
                Constraint("REQUIRED_FEATURE", ProductSystemConstraintType.RequiredFeature,
                    allowedValues: ["POCKET"]),
                Constraint("FORBIDDEN_FEATURE", ProductSystemConstraintType.ForbiddenFeature,
                    allowedValues: ["INOX"])),
            Input(operation: "sliding", geometryType: "RECTANGULAR",
                features: ["pocket"]),
            ConstraintEvaluationStage.PreSelection);

        Assert.All(result.Evaluations, value =>
            Assert.Equal(ProductSystemConstraintEvaluationState.Pass, value.State));
    }

    [Fact]
    public void Evaluate_IgnoresInactiveAndExpiredConstraints()
    {
        var result = Evaluator().Evaluate(
            System(
                Constraint("INACTIVE", ProductSystemConstraintType.MaxWidth,
                    maxValue: 1m, isActive: false),
                Constraint("EXPIRED", ProductSystemConstraintType.MaxWidth,
                    maxValue: 1m, effectiveToUtc: Now.AddDays(-1))),
            Input(width: 5000),
            ConstraintEvaluationStage.PreSelection);

        Assert.Empty(result.Evaluations);
    }

    [Fact]
    public void Evaluate_PostDesignLeafConstraint_IsDeferredInPreSelection()
    {
        var result = Evaluator().Evaluate(
            System(Constraint(
                "MAX_LEAF_WIDTH",
                ProductSystemConstraintType.MaxLeafWidth,
                scope: ProductSystemConstraintScope.Leaf,
                evaluationStage: ConstraintEvaluationStage.PostDesign,
                maxValue: 1000m,
                severity: ProductSystemConstraintSeverity.Hard,
                knowledgeClass: ProductSystemConstraintKnowledgeClass.VerifiedTechnical)),
            Input(width: 4000, panelCount: 4),
            ConstraintEvaluationStage.PreSelection);

        Assert.True(result.HasDeferred);
        Assert.False(result.HasHardFailure);
        Assert.DoesNotContain(result.Evaluations, value =>
            value.State == ProductSystemConstraintEvaluationState.Fail);
    }

    private static SgProductSystemConstraintEvaluator Evaluator() =>
        new(new FixedTimeProvider(Now));

    private static ProductSystemCatalogReadModel System(
        params ProductSystemConstraintCatalogReadModel[] constraints) =>
        new(
            Guid.NewGuid(),
            "K70",
            "K70",
            "K70",
            "K70",
            "SLIDING_DOOR",
            "VENECIA NAPOLES",
            "70",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            false,
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
        bool requiresReviewWhenUnknown = false,
        bool isActive = true,
        DateTimeOffset? effectiveToUtc = null) =>
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
            isActive,
            null,
            effectiveToUtc,
            ProductSystemConstraintSourceType.SgRule,
            null,
            null);

    private static SgTechnicalSelectionInput Input(
        int? width = 1200,
        int? height = 1500,
        decimal? area = null,
        int? panelCount = null,
        string? operation = null,
        string? geometryType = null,
        IReadOnlyList<string>? features = null) =>
        new(
            "SLIDING_DOOR",
            operation,
            width,
            height,
            area,
            panelCount,
            null,
            null,
            null,
            null,
            features ?? [],
            geometryType,
            null,
            null);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
