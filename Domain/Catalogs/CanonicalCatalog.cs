using System.Globalization;
using System.Text;

namespace Domain.Catalogs;

public enum CatalogAliasCategory
{
    System = 1,
    Frame,
    Finish
}

public enum CatalogAliasMatchPolicy
{
    ExactNormalized = 1,
    TechnicalPhrase
}

public enum ProductSystemConstraintType
{
    MinWidth = 1,
    MaxWidth,
    MinHeight,
    MaxHeight,
    MinArea,
    MaxArea,
    MinPanelCount,
    MaxPanelCount,
    MinMovablePanelCount,
    MaxMovablePanelCount,
    MinFixedPanelCount,
    MaxFixedPanelCount,
    AllowedOperation,
    AllowedGeometry,
    ForbiddenGeometry,
    RequiredFeature,
    ForbiddenFeature,
    MinLeafWidth,
    MaxLeafWidth,
    MinLeafHeight,
    MaxLeafHeight,
    MinPanelWidth,
    MaxPanelWidth,
    MinPanelHeight,
    MaxPanelHeight
}

public enum ProductSystemConstraintScope
{
    System = 1,
    Opening,
    Panel,
    MovablePanel,
    FixedPanel,
    Leaf
}

public enum ConstraintEvaluationStage
{
    PreSelection = 1,
    PostDesign
}

public enum ProductSystemConstraintSeverity
{
    Hard = 1,
    Review
}

public enum ProductSystemConstraintKnowledgeClass
{
    VerifiedTechnical = 1,
    Calibration,
    Preference,
    Unknown
}

public enum ProductSystemConstraintSourceType
{
    Manufacturer = 1,
    SgRule,
    HistoricalCalibration,
    Manual,
    Other
}

public sealed class ProductSystem
{
    private readonly List<ProductSystemConstraint> _constraints = [];

    private ProductSystem() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? TechnicalName { get; private set; }
    public string? CommercialName { get; private set; }
    public string? FunctionalType { get; private set; }
    public string? Family { get; private set; }
    public string? Series { get; private set; }
    public string? CommercialLine { get; private set; }
    public string? Variant { get; private set; }
    public bool IsSelectable { get; private set; }
    public bool ActiveForRecognition { get; private set; }
    public bool Priceable { get; private set; }
    public bool FuturePriceable { get; private set; }
    public bool RequiresReview { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ProductSystemConstraint> Constraints => _constraints;

    public static ProductSystem Create(
        string code,
        string name,
        bool activeForRecognition,
        bool priceable,
        bool futurePriceable,
        bool requiresReview,
        DateTimeOffset createdAtUtc,
        string? technicalName = null,
        string? commercialName = null,
        string? functionalType = null,
        string? family = null,
        string? series = null,
        string? commercialLine = null,
        string? variant = null,
        bool isSelectable = false)
    {
        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new ProductSystem
        {
            Id = Guid.NewGuid(),
            Code = CatalogText.NormalizeCode(code, nameof(code)),
            Name = CatalogText.RequiredText(name, 100, nameof(name)),
            TechnicalName = CatalogText.OptionalText(technicalName, 100, nameof(technicalName)),
            CommercialName = CatalogText.OptionalText(commercialName, 100, nameof(commercialName)),
            FunctionalType = CatalogText.OptionalText(functionalType, 60, nameof(functionalType)),
            Family = CatalogText.OptionalText(family, 60, nameof(family)),
            Series = CatalogText.OptionalText(series, 60, nameof(series)),
            CommercialLine = CatalogText.OptionalText(commercialLine, 60, nameof(commercialLine)),
            Variant = CatalogText.OptionalText(variant, 60, nameof(variant)),
            IsSelectable = isSelectable,
            ActiveForRecognition = activeForRecognition,
            Priceable = priceable,
            FuturePriceable = futurePriceable,
            RequiresReview = requiresReview,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }
}


public sealed class ProductSystemConstraint
{
    private ProductSystemConstraint() { }

    public Guid Id { get; private set; }
    public Guid ProductSystemId { get; private set; }
    public ProductSystem ProductSystem { get; private set; } = null!;
    public string Code { get; private set; } = string.Empty;
    public ProductSystemConstraintType ConstraintType { get; private set; }
    public ProductSystemConstraintScope Scope { get; private set; }
    public ConstraintEvaluationStage EvaluationStage { get; private set; }
    public ProductSystemConstraintSeverity Severity { get; private set; }
    public ProductSystemConstraintKnowledgeClass KnowledgeClass { get; private set; }
    public decimal? MinValue { get; private set; }
    public decimal? MaxValue { get; private set; }
    public string? TextValue { get; private set; }
    public string[] AllowedValues { get; private set; } = [];
    public string? Unit { get; private set; }
    public bool RequiresReviewWhenUnknown { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public ProductSystemConstraintSourceType SourceType { get; private set; }
    public string? SourceReference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static ProductSystemConstraint Create(
        Guid productSystemId,
        string code,
        ProductSystemConstraintType constraintType,
        ProductSystemConstraintScope scope,
        ConstraintEvaluationStage evaluationStage,
        ProductSystemConstraintSeverity severity,
        ProductSystemConstraintKnowledgeClass knowledgeClass,
        bool requiresReviewWhenUnknown,
        ProductSystemConstraintSourceType sourceType,
        DateTimeOffset createdAtUtc,
        decimal? minValue = null,
        decimal? maxValue = null,
        string? textValue = null,
        IReadOnlyList<string>? allowedValues = null,
        string? unit = null,
        DateTimeOffset? effectiveFromUtc = null,
        DateTimeOffset? effectiveToUtc = null,
        string? sourceReference = null,
        string? notes = null,
        bool isActive = true)
    {
        if (productSystemId == Guid.Empty)
        {
            throw new ArgumentException("Sistema obligatorio.", nameof(productSystemId));
        }

        if (!Enum.IsDefined(constraintType)
            || !Enum.IsDefined(scope)
            || !Enum.IsDefined(evaluationStage)
            || !Enum.IsDefined(severity)
            || !Enum.IsDefined(knowledgeClass)
            || !Enum.IsDefined(sourceType))
        {
            throw new ArgumentException("Restriccion tecnica invalida.");
        }

        if (severity == ProductSystemConstraintSeverity.Hard
            && knowledgeClass != ProductSystemConstraintKnowledgeClass.VerifiedTechnical)
        {
            throw new ArgumentException("Solo una restriccion tecnica verificada puede ser HARD.");
        }

        if (minValue is not null && maxValue is not null && minValue > maxValue)
        {
            throw new ArgumentException("Rango de restriccion invalido.");
        }

        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureOptionalUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        EnsureOptionalUtc(effectiveToUtc, nameof(effectiveToUtc));
        if (effectiveFromUtc is not null
            && effectiveToUtc is not null
            && effectiveFromUtc > effectiveToUtc)
        {
            throw new ArgumentException("Vigencia de restriccion invalida.");
        }

        return new ProductSystemConstraint
        {
            Id = Guid.NewGuid(),
            ProductSystemId = productSystemId,
            Code = CatalogText.NormalizeCode(code, nameof(code)),
            ConstraintType = constraintType,
            Scope = scope,
            EvaluationStage = evaluationStage,
            Severity = severity,
            KnowledgeClass = knowledgeClass,
            MinValue = minValue,
            MaxValue = maxValue,
            TextValue = CatalogText.OptionalText(textValue, 100, nameof(textValue)),
            AllowedValues = NormalizeAllowedValues(allowedValues ?? []),
            Unit = CatalogText.OptionalText(unit, 20, nameof(unit)),
            RequiresReviewWhenUnknown = requiresReviewWhenUnknown,
            IsActive = isActive,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            SourceType = sourceType,
            SourceReference = CatalogText.OptionalText(sourceReference, 200, nameof(sourceReference)),
            Notes = CatalogText.OptionalText(notes, 500, nameof(notes)),
            CreatedAtUtc = createdAtUtc
        };
    }

    public bool IsApplicableAt(DateTimeOffset atUtc)
    {
        CatalogText.EnsureUtc(atUtc, nameof(atUtc));
        return IsActive
            && (EffectiveFromUtc is null || EffectiveFromUtc <= atUtc)
            && (EffectiveToUtc is null || EffectiveToUtc >= atUtc);
    }

    private static string[] NormalizeAllowedValues(IReadOnlyList<string> values) =>
        values
            .Select(value => CatalogText.NormalizeCode(value, nameof(values)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void EnsureOptionalUtc(DateTimeOffset? value, string name)
    {
        if (value is { } date)
        {
            CatalogText.EnsureUtc(date, name);
        }
    }
}
public sealed class FrameType
{
    private FrameType() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static FrameType Create(
        string code,
        string name,
        DateTimeOffset createdAtUtc)
    {
        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new FrameType
        {
            Id = Guid.NewGuid(),
            Code = CatalogText.NormalizeCode(code, nameof(code)),
            Name = CatalogText.RequiredText(name, 100, nameof(name)),
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }
}

public sealed class FinishType
{
    private FinishType() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool RequiresReview { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static FinishType Create(
        string code,
        string name,
        bool requiresReview,
        DateTimeOffset createdAtUtc)
    {
        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new FinishType
        {
            Id = Guid.NewGuid(),
            Code = CatalogText.NormalizeCode(code, nameof(code)),
            Name = CatalogText.RequiredText(name, 100, nameof(name)),
            RequiresReview = requiresReview,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }
}

public sealed class CatalogAlias
{
    private CatalogAlias() { }

    public Guid Id { get; private set; }
    public CatalogAliasCategory Category { get; private set; }
    public string Alias { get; private set; } = string.Empty;
    public string NormalizedAlias { get; private set; } = string.Empty;
    public string CanonicalCode { get; private set; } = string.Empty;
    public CatalogAliasMatchPolicy MatchPolicy { get; private set; }
    public bool RequiresContext { get; private set; }
    public decimal Confidence { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static CatalogAlias Create(
        CatalogAliasCategory category,
        string alias,
        string canonicalCode,
        CatalogAliasMatchPolicy matchPolicy,
        bool requiresContext,
        decimal confidence,
        DateTimeOffset createdAtUtc)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentException("Categoria invalida.", nameof(category));
        }
        if (!Enum.IsDefined(matchPolicy))
        {
            throw new ArgumentException("Politica de alias invalida.", nameof(matchPolicy));
        }
        if (confidence < 0 || confidence > 1)
        {
            throw new ArgumentException("Confianza invalida.", nameof(confidence));
        }

        var normalizedAlias = CatalogAliasNormalizer.Normalize(alias);
        if (normalizedAlias.All(char.IsDigit))
        {
            throw new ArgumentException("Alias numerico invalido.", nameof(alias));
        }

        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new CatalogAlias
        {
            Id = Guid.NewGuid(),
            Category = category,
            Alias = CatalogText.RequiredText(alias, 200, nameof(alias)),
            NormalizedAlias = normalizedAlias,
            CanonicalCode = CatalogText.NormalizeCode(canonicalCode, nameof(canonicalCode)),
            MatchPolicy = matchPolicy,
            RequiresContext = requiresContext,
            Confidence = confidence,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }
}

public static class CatalogAliasNormalizer
{
    public static string Normalize(string value)
    {
        var text = CatalogText.RequiredText(value, 200, nameof(value))
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        var previousWasWhiteSpace = false;

        foreach (var character in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }
                continue;
            }

            builder.Append(character);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}

internal static class CatalogText
{
    public static string NormalizeCode(string? value, string name)
    {
        var code = RequiredText(value, 30, name).ToUpperInvariant();
        if (!code.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '_' or '-'))
        {
            throw new ArgumentException("Codigo de catalogo invalido.", name);
        }

        return code;
    }

    public static string RequiredText(
        string? value,
        int maximumLength,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Valor obligatorio.", name);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException("Valor demasiado largo.", name);
        }

        return normalized;
    }

    public static string? OptionalText(
        string? value,
        int maximumLength,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequiredText(value, maximumLength, name);
    }

    public static void EnsureUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La fecha debe estar en UTC.", name);
        }
    }
}
