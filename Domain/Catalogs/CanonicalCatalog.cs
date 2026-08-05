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

public sealed class ProductSystem
{
    private ProductSystem() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool ActiveForRecognition { get; private set; }
    public bool Priceable { get; private set; }
    public bool FuturePriceable { get; private set; }
    public bool RequiresReview { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static ProductSystem Create(
        string code,
        string name,
        bool activeForRecognition,
        bool priceable,
        bool futurePriceable,
        bool requiresReview,
        DateTimeOffset createdAtUtc)
    {
        CatalogText.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new ProductSystem
        {
            Id = Guid.NewGuid(),
            Code = CatalogText.NormalizeCode(code, nameof(code)),
            Name = CatalogText.RequiredText(name, 100, nameof(name)),
            ActiveForRecognition = activeForRecognition,
            Priceable = priceable,
            FuturePriceable = futurePriceable,
            RequiresReview = requiresReview,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
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

    public static void EnsureUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La fecha debe estar en UTC.", name);
        }
    }
}
