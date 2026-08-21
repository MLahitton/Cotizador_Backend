namespace Domain.Catalogs;

public enum GlassPriceRangeStatus
{
    Preliminary = 1,
    Active,
    Retired
}

public sealed class GlassType
{
    public const int NameMaximumLength = 200;

    private readonly List<GlassPriceRangeVersion> _priceRangeVersions = [];

    private GlassType() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Family { get; private set; }
    public string? Composition { get; private set; }
    public string? Treatment { get; private set; }
    public decimal? OuterThicknessMm { get; private set; }
    public decimal? InnerThicknessMm { get; private set; }
    public decimal? PvbThicknessMm { get; private set; }
    public string? PvbType { get; private set; }
    public string? PvbColor { get; private set; }
    public decimal? ChamberThicknessMm { get; private set; }
    public string? ProductLine { get; private set; }
    public string? ProductToken { get; private set; }
    public string? Pattern { get; private set; }
    public string? Color { get; private set; }
    public bool IsSelectable { get; private set; }
    public bool RequiresReview { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<GlassPriceRangeVersion> PriceRangeVersions =>
        _priceRangeVersions;

    public static GlassType Create(
        string code,
        string name,
        string? description,
        DateTimeOffset createdAtUtc,
        string? family = null,
        string? composition = null,
        string? treatment = null,
        decimal? outerThicknessMm = null,
        decimal? innerThicknessMm = null,
        decimal? pvbThicknessMm = null,
        string? pvbType = null,
        string? pvbColor = null,
        decimal? chamberThicknessMm = null,
        string? productLine = null,
        string? productToken = null,
        string? pattern = null,
        string? color = null,
        bool isSelectable = true,
        bool requiresReview = false)
    {
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        return new GlassType
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = RequiredText(name, NameMaximumLength, nameof(name)),
            Description = OptionalText(description, 500, nameof(description)),
            Family = OptionalText(family, 40, nameof(family)),
            Composition = OptionalText(composition, 40, nameof(composition)),
            Treatment = OptionalText(treatment, 40, nameof(treatment)),
            OuterThicknessMm = PositiveDecimal(
                outerThicknessMm,
                nameof(outerThicknessMm)),
            InnerThicknessMm = PositiveDecimal(
                innerThicknessMm,
                nameof(innerThicknessMm)),
            PvbThicknessMm = PositiveDecimal(
                pvbThicknessMm,
                nameof(pvbThicknessMm)),
            PvbType = OptionalText(pvbType, 40, nameof(pvbType)),
            PvbColor = OptionalText(pvbColor, 40, nameof(pvbColor)),
            ChamberThicknessMm = PositiveDecimal(
                chamberThicknessMm,
                nameof(chamberThicknessMm)),
            ProductLine = OptionalText(productLine, 80, nameof(productLine)),
            ProductToken = OptionalText(productToken, 40, nameof(productToken)),
            Pattern = OptionalText(pattern, 80, nameof(pattern)),
            Color = OptionalText(color, 40, nameof(color)),
            IsSelectable = isSelectable,
            RequiresReview = requiresReview,
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

    private static decimal? PositiveDecimal(decimal? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        if (value <= 0)
        {
            throw new ArgumentException("Valor numerico invalido.", name);
        }

        return value;
    }
}

public sealed class GlassPriceRangeVersion
{
    private GlassPriceRangeVersion() { }

    public Guid Id { get; private set; }
    public Guid GlassTypeId { get; private set; }
    public int Version { get; private set; }
    public decimal MinimumPricePerSquareMeter { get; private set; }
    public decimal ExpectedAmountPerM2 { get; private set; }
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
        decimal expectedAmountPerM2,
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
        if (expectedAmountPerM2 <= 0
            || expectedAmountPerM2 < minimumPricePerSquareMeter
            || expectedAmountPerM2 > maximumPricePerSquareMeter)
        {
            throw new ArgumentException("Precio esperado invalido.", nameof(expectedAmountPerM2));
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
            ExpectedAmountPerM2 = expectedAmountPerM2,
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
