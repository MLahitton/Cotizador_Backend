namespace Domain.Catalogs;

public enum GlassPriceRangeStatus
{
    Preliminary = 1,
    Active,
    Retired
}

public sealed class GlassType
{
    private readonly List<GlassPriceRangeVersion> _priceRangeVersions = [];

    private GlassType() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<GlassPriceRangeVersion> PriceRangeVersions =>
        _priceRangeVersions;

    public static GlassType Create(
        string code,
        string name,
        string? description,
        DateTimeOffset createdAtUtc)
    {
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new GlassType
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = RequiredText(name, 100, nameof(name)),
            Description = OptionalText(description, 500, nameof(description)),
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    internal static string NormalizeCode(string? value)
    {
        var code = RequiredText(value, 30, nameof(value)).ToUpperInvariant();
        if (!code.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '_' or '-'))
        {
            throw new ArgumentException("Codigo de vidrio invalido.", nameof(value));
        }

        return code;
    }

    internal static void EnsureUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("La fecha debe estar en UTC.", name);
        }
    }

    private static string RequiredText(
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

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException("Valor demasiado largo.", name);
        }

        return normalized;
    }
}

public sealed class GlassPriceRangeVersion
{
    private GlassPriceRangeVersion() { }

    public Guid Id { get; private set; }
    public Guid GlassTypeId { get; private set; }
    public int Version { get; private set; }
    public decimal MinimumPricePerSquareMeter { get; private set; }
    public decimal MaximumPricePerSquareMeter { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public GlassPriceRangeStatus Status { get; private set; }
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidToUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public GlassType GlassType { get; private set; } = null!;

    public static GlassPriceRangeVersion Create(
        Guid glassTypeId,
        int version,
        decimal minimumPricePerSquareMeter,
        decimal maximumPricePerSquareMeter,
        string currency,
        GlassPriceRangeStatus status,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validToUtc,
        DateTimeOffset createdAtUtc)
    {
        if (glassTypeId == Guid.Empty)
        {
            throw new ArgumentException("Tipo de vidrio obligatorio.", nameof(glassTypeId));
        }
        if (version <= 0)
        {
            throw new ArgumentException("Version invalida.", nameof(version));
        }
        if (minimumPricePerSquareMeter <= 0)
        {
            throw new ArgumentException("Precio minimo invalido.", nameof(minimumPricePerSquareMeter));
        }
        if (maximumPricePerSquareMeter <= 0
            || maximumPricePerSquareMeter < minimumPricePerSquareMeter)
        {
            throw new ArgumentException("Precio maximo invalido.", nameof(maximumPricePerSquareMeter));
        }
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException("Estado invalido.", nameof(status));
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        GlassType.EnsureUtc(validFromUtc, nameof(validFromUtc));
        GlassType.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (validToUtc is { } validTo)
        {
            GlassType.EnsureUtc(validTo, nameof(validToUtc));
            if (validTo <= validFromUtc)
            {
                throw new ArgumentException("Vigencia final invalida.", nameof(validToUtc));
            }
        }

        return new GlassPriceRangeVersion
        {
            Id = Guid.NewGuid(),
            GlassTypeId = glassTypeId,
            Version = version,
            MinimumPricePerSquareMeter = minimumPricePerSquareMeter,
            MaximumPricePerSquareMeter = maximumPricePerSquareMeter,
            Currency = normalizedCurrency,
            Status = status,
            ValidFromUtc = validFromUtc,
            ValidToUtc = validToUtc,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Moneda obligatoria.", nameof(value));
        }

        var currency = value.Trim().ToUpperInvariant();
        if (currency.Length != 3
            || !currency.All(character => character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException("Moneda invalida.", nameof(value));
        }

        return currency;
    }
}
