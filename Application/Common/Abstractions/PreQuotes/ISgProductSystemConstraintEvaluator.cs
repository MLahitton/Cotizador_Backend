using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;

namespace Application.Common.Abstractions.PreQuotes;

public interface ISgProductSystemConstraintEvaluator
{
    SgProductSystemConstraintEvaluationResult Evaluate(
        ProductSystemCatalogReadModel productSystem,
        SgTechnicalSelectionInput input,
        ConstraintEvaluationStage evaluationStage);
}

public sealed record SgProductSystemConstraintEvaluationResult(
    IReadOnlyList<SgProductSystemConstraintEvaluation> Evaluations)
{
    public bool HasHardFailure => Evaluations.Any(value =>
        value.State == ProductSystemConstraintEvaluationState.Fail
        && value.Severity == ProductSystemConstraintSeverity.Hard
        && value.KnowledgeClass == ProductSystemConstraintKnowledgeClass.VerifiedTechnical);

    public bool HasReviewFailure => Evaluations.Any(value =>
        value.State == ProductSystemConstraintEvaluationState.Fail
        && value.Severity == ProductSystemConstraintSeverity.Review);

    public bool HasUnknownReview => Evaluations.Any(value =>
        value.State == ProductSystemConstraintEvaluationState.Unknown
        && value.RequiresReviewWhenUnknown);

    public bool HasDeferred => Evaluations.Any(value =>
        value.State == ProductSystemConstraintEvaluationState.Deferred);

    public IReadOnlyList<string> ReviewReasons => Evaluations
        .Where(value =>
            value.State == ProductSystemConstraintEvaluationState.Fail
                && value.Severity == ProductSystemConstraintSeverity.Review
            || value.State == ProductSystemConstraintEvaluationState.Unknown
                && value.RequiresReviewWhenUnknown)
        .Select(value => value.ReviewReason)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

public sealed record SgProductSystemConstraintEvaluation(
    string ConstraintCode,
    ProductSystemConstraintType ConstraintType,
    ProductSystemConstraintScope Scope,
    ConstraintEvaluationStage EvaluationStage,
    ProductSystemConstraintSeverity Severity,
    ProductSystemConstraintKnowledgeClass KnowledgeClass,
    ProductSystemConstraintEvaluationState State,
    bool RequiresReviewWhenUnknown,
    string ReviewReason);

public enum ProductSystemConstraintEvaluationState
{
    Pass = 1,
    Fail,
    Unknown,
    Deferred
}

public sealed class SgProductSystemConstraintEvaluator(
    TimeProvider timeProvider) : ISgProductSystemConstraintEvaluator
{
    public SgProductSystemConstraintEvaluationResult Evaluate(
        ProductSystemCatalogReadModel productSystem,
        SgTechnicalSelectionInput input,
        ConstraintEvaluationStage evaluationStage)
    {
        var now = timeProvider.GetUtcNow();
        var evaluations = productSystem.Constraints
            .Where(constraint => IsApplicableAt(constraint, now))
            .Select(constraint => EvaluateConstraint(
                constraint, input, evaluationStage))
            .ToArray();
        return new(evaluations);
    }

    private static SgProductSystemConstraintEvaluation EvaluateConstraint(
        ProductSystemConstraintCatalogReadModel constraint,
        SgTechnicalSelectionInput input,
        ConstraintEvaluationStage requestedStage)
    {
        if (constraint.EvaluationStage != requestedStage)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Deferred);
        }

        return constraint.ConstraintType switch
        {
            ProductSystemConstraintType.MinWidth => Numeric(
                constraint, input.WidthMillimeters, minimum: true),
            ProductSystemConstraintType.MaxWidth => Numeric(
                constraint, input.WidthMillimeters, minimum: false),
            ProductSystemConstraintType.MinHeight => Numeric(
                constraint, input.HeightMillimeters, minimum: true),
            ProductSystemConstraintType.MaxHeight => Numeric(
                constraint, input.HeightMillimeters, minimum: false),
            ProductSystemConstraintType.MinArea => Numeric(
                constraint, input.AreaSquareMeters, minimum: true),
            ProductSystemConstraintType.MaxArea => Numeric(
                constraint, input.AreaSquareMeters, minimum: false),
            ProductSystemConstraintType.MinPanelCount => Numeric(
                constraint, input.PanelCount, minimum: true),
            ProductSystemConstraintType.MaxPanelCount => Numeric(
                constraint, input.PanelCount, minimum: false),
            ProductSystemConstraintType.MinMovablePanelCount => Numeric(
                constraint, input.MovablePanelCount, minimum: true),
            ProductSystemConstraintType.MaxMovablePanelCount => Numeric(
                constraint, input.MovablePanelCount, minimum: false),
            ProductSystemConstraintType.MinFixedPanelCount => Numeric(
                constraint, input.FixedPanelCount, minimum: true),
            ProductSystemConstraintType.MaxFixedPanelCount => Numeric(
                constraint, input.FixedPanelCount, minimum: false),
            ProductSystemConstraintType.AllowedOperation => AllowedCode(
                constraint, input.Operation),
            ProductSystemConstraintType.AllowedGeometry => AllowedCode(
                constraint, input.GeometryType),
            ProductSystemConstraintType.ForbiddenGeometry => ForbiddenCode(
                constraint, input.GeometryType),
            ProductSystemConstraintType.RequiredFeature => RequiredFeature(
                constraint, input.SpecialFeatures),
            ProductSystemConstraintType.ForbiddenFeature => ForbiddenFeature(
                constraint, input.SpecialFeatures),
            ProductSystemConstraintType.MinLeafWidth
                or ProductSystemConstraintType.MaxLeafWidth
                or ProductSystemConstraintType.MinLeafHeight
                or ProductSystemConstraintType.MaxLeafHeight
                or ProductSystemConstraintType.MinPanelWidth
                or ProductSystemConstraintType.MaxPanelWidth
                or ProductSystemConstraintType.MinPanelHeight
                or ProductSystemConstraintType.MaxPanelHeight => Evaluation(
                    constraint,
                    ProductSystemConstraintEvaluationState.Deferred),
            _ => Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown)
        };
    }

    private static SgProductSystemConstraintEvaluation Numeric(
        ProductSystemConstraintCatalogReadModel constraint,
        int? value,
        bool minimum) =>
        Numeric(
            constraint,
            value is null ? null : (decimal)value.Value,
            minimum);

    private static SgProductSystemConstraintEvaluation Numeric(
        ProductSystemConstraintCatalogReadModel constraint,
        decimal? value,
        bool minimum)
    {
        if (value is null)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown);
        }

        var limit = minimum ? constraint.MinValue : constraint.MaxValue;
        if (limit is null)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown);
        }

        var passes = minimum ? value >= limit : value <= limit;
        return Evaluation(
            constraint,
            passes
                ? ProductSystemConstraintEvaluationState.Pass
                : ProductSystemConstraintEvaluationState.Fail);
    }

    private static SgProductSystemConstraintEvaluation AllowedCode(
        ProductSystemConstraintCatalogReadModel constraint,
        string? value)
    {
        var code = Code(value);
        if (code is null)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown);
        }

        return Evaluation(
            constraint,
            constraint.AllowedValues.Contains(code, StringComparer.Ordinal)
                ? ProductSystemConstraintEvaluationState.Pass
                : ProductSystemConstraintEvaluationState.Fail);
    }

    private static SgProductSystemConstraintEvaluation ForbiddenCode(
        ProductSystemConstraintCatalogReadModel constraint,
        string? value)
    {
        var code = Code(value);
        if (code is null)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown);
        }

        return Evaluation(
            constraint,
            constraint.AllowedValues.Contains(code, StringComparer.Ordinal)
                ? ProductSystemConstraintEvaluationState.Fail
                : ProductSystemConstraintEvaluationState.Pass);
    }

    private static SgProductSystemConstraintEvaluation RequiredFeature(
        ProductSystemConstraintCatalogReadModel constraint,
        IReadOnlyList<string> values)
    {
        var features = Features(values);
        if (features.Count == 0)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Unknown);
        }

        return Evaluation(
            constraint,
            constraint.AllowedValues.Any(value => features.Contains(value))
                ? ProductSystemConstraintEvaluationState.Pass
                : ProductSystemConstraintEvaluationState.Fail);
    }

    private static SgProductSystemConstraintEvaluation ForbiddenFeature(
        ProductSystemConstraintCatalogReadModel constraint,
        IReadOnlyList<string> values)
    {
        var features = Features(values);
        if (features.Count == 0)
        {
            return Evaluation(
                constraint,
                ProductSystemConstraintEvaluationState.Pass);
        }

        return Evaluation(
            constraint,
            constraint.AllowedValues.Any(value => features.Contains(value))
                ? ProductSystemConstraintEvaluationState.Fail
                : ProductSystemConstraintEvaluationState.Pass);
    }

    private static SgProductSystemConstraintEvaluation Evaluation(
        ProductSystemConstraintCatalogReadModel constraint,
        ProductSystemConstraintEvaluationState state) =>
        new(
            constraint.Code,
            constraint.ConstraintType,
            constraint.Scope,
            constraint.EvaluationStage,
            constraint.Severity,
            constraint.KnowledgeClass,
            state,
            constraint.RequiresReviewWhenUnknown,
            $"SYSTEM_CONSTRAINT_{constraint.Code}_{state.ToString().ToUpperInvariant()}");

    private static bool IsApplicableAt(
        ProductSystemConstraintCatalogReadModel constraint,
        DateTimeOffset at) =>
        constraint.IsActive
        && (constraint.EffectiveFromUtc is null || constraint.EffectiveFromUtc <= at)
        && (constraint.EffectiveToUtc is null || constraint.EffectiveToUtc >= at);

    private static IReadOnlySet<string> Features(IReadOnlyList<string> values) =>
        values.Select(Code)
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static string? Code(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
}
