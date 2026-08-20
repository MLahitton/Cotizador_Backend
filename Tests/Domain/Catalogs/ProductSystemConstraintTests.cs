using Domain.Catalogs;
using Xunit;

namespace CotizadorBackend.Tests.Domain.Catalogs;

public sealed class ProductSystemConstraintTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithVerifiedHardConstraint_IsValid()
    {
        var constraint = ProductSystemConstraint.Create(
            Guid.NewGuid(),
            "MAX_OPENING_WIDTH",
            ProductSystemConstraintType.MaxWidth,
            ProductSystemConstraintScope.Opening,
            ConstraintEvaluationStage.PreSelection,
            ProductSystemConstraintSeverity.Hard,
            ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
            requiresReviewWhenUnknown: false,
            ProductSystemConstraintSourceType.SgRule,
            Now,
            maxValue: 3000m,
            unit: "mm");

        Assert.Equal("MAX_OPENING_WIDTH", constraint.Code);
        Assert.True(constraint.IsApplicableAt(Now));
    }

    [Theory]
    [InlineData(ProductSystemConstraintKnowledgeClass.Calibration)]
    [InlineData(ProductSystemConstraintKnowledgeClass.Preference)]
    [InlineData(ProductSystemConstraintKnowledgeClass.Unknown)]
    public void Create_WithHardConstraintNotVerified_Rejects(
        ProductSystemConstraintKnowledgeClass knowledgeClass)
    {
        Assert.Throws<ArgumentException>(() => ProductSystemConstraint.Create(
            Guid.NewGuid(),
            "MAX_OPENING_WIDTH",
            ProductSystemConstraintType.MaxWidth,
            ProductSystemConstraintScope.Opening,
            ConstraintEvaluationStage.PreSelection,
            ProductSystemConstraintSeverity.Hard,
            knowledgeClass,
            requiresReviewWhenUnknown: false,
            ProductSystemConstraintSourceType.SgRule,
            Now,
            maxValue: 3000m));
    }

    [Theory]
    [InlineData(ConstraintEvaluationStage.PreSelection)]
    [InlineData(ConstraintEvaluationStage.PostDesign)]
    public void Create_WithSupportedEvaluationStage_IsValid(
        ConstraintEvaluationStage stage)
    {
        var constraint = ProductSystemConstraint.Create(
            Guid.NewGuid(),
            "MAX_OPENING_WIDTH",
            ProductSystemConstraintType.MaxWidth,
            ProductSystemConstraintScope.Opening,
            stage,
            ProductSystemConstraintSeverity.Review,
            ProductSystemConstraintKnowledgeClass.Calibration,
            requiresReviewWhenUnknown: true,
            ProductSystemConstraintSourceType.HistoricalCalibration,
            Now,
            maxValue: 3000m);

        Assert.Equal(stage, constraint.EvaluationStage);
    }

    [Fact]
    public void IsApplicableAt_RespectsActivityAndEffectiveRange()
    {
        var active = ProductSystemConstraint.Create(
            Guid.NewGuid(),
            "MAX_OPENING_WIDTH",
            ProductSystemConstraintType.MaxWidth,
            ProductSystemConstraintScope.Opening,
            ConstraintEvaluationStage.PreSelection,
            ProductSystemConstraintSeverity.Review,
            ProductSystemConstraintKnowledgeClass.Calibration,
            requiresReviewWhenUnknown: true,
            ProductSystemConstraintSourceType.HistoricalCalibration,
            Now,
            maxValue: 3000m,
            effectiveFromUtc: Now.AddDays(-1),
            effectiveToUtc: Now.AddDays(1));
        var inactive = ProductSystemConstraint.Create(
            Guid.NewGuid(),
            "MAX_OPENING_WIDTH",
            ProductSystemConstraintType.MaxWidth,
            ProductSystemConstraintScope.Opening,
            ConstraintEvaluationStage.PreSelection,
            ProductSystemConstraintSeverity.Review,
            ProductSystemConstraintKnowledgeClass.Calibration,
            requiresReviewWhenUnknown: true,
            ProductSystemConstraintSourceType.HistoricalCalibration,
            Now,
            maxValue: 3000m,
            isActive: false);

        Assert.True(active.IsApplicableAt(Now));
        Assert.False(active.IsApplicableAt(Now.AddDays(2)));
        Assert.False(inactive.IsApplicableAt(Now));
    }
}
