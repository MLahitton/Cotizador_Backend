using Domain.Identity;

namespace Domain.PreQuotes;

public sealed class RequirementExperienceConfigurationConfirmation
{
    private RequirementExperienceConfigurationConfirmation() { }

    private readonly List<RequirementExperienceConfigurationConfirmationItem>
        _items = [];

    public Guid Id { get; private set; }

    public Guid RequirementId { get; private set; }

    public Requirement Requirement { get; private set; } = null!;

    public Guid TechnicalProposalId { get; private set; }

    public RequirementTechnicalProposal TechnicalProposal { get; private set; } = null!;

    public long Revision { get; private set; }

    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    public Guid ConfirmedByUserId { get; private set; }

    public User ConfirmedByUser { get; private set; } = null!;

    public IReadOnlyCollection<RequirementExperienceConfigurationConfirmationItem>
        Items => _items;

    public static RequirementExperienceConfigurationConfirmation Create(
        Guid requirementId,
        Guid technicalProposalId,
        long revision,
        Guid confirmedByUserId,
        DateTimeOffset confirmedAtUtc)
    {
        EnsureRequired(requirementId, nameof(requirementId));
        EnsureRequired(technicalProposalId, nameof(technicalProposalId));
        EnsureRequired(confirmedByUserId, nameof(confirmedByUserId));
        EnsureRevision(revision);
        Requirement.EnsureUtc(confirmedAtUtc, nameof(confirmedAtUtc));

        return new RequirementExperienceConfigurationConfirmation
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            TechnicalProposalId = technicalProposalId,
            Revision = revision,
            ConfirmedAtUtc = confirmedAtUtc,
            ConfirmedByUserId = confirmedByUserId
        };
    }

    public void AddItem(RequirementExperienceConfigurationConfirmationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ConfirmationId != Id)
        {
            throw new ArgumentException(
                "El item no pertenece a la confirmacion.",
                nameof(item));
        }

        if (_items.Any(current =>
                current.TechnicalProposalItemId == item.TechnicalProposalItemId))
        {
            throw new InvalidOperationException(
                "Ya existe un snapshot para el item tecnico.");
        }

        _items.Add(item);
    }

    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    internal static string NormalizeRequired(
        string value,
        string parameterName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "El valor es obligatorio.",
                parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    internal static string[] NormalizeReasons(
        IReadOnlyList<string> reasons,
        int maxLength = 120)
    {
        return reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => NormalizeRequired(reason, nameof(reasons), maxLength))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnsureRevision(long revision)
    {
        if (revision <= 0)
        {
            throw new ArgumentException(
                "La revision debe ser mayor que cero.",
                nameof(revision));
        }
    }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador es obligatorio.",
                parameterName);
        }
    }
}

public sealed class RequirementExperienceConfigurationConfirmationItem
{
    private RequirementExperienceConfigurationConfirmationItem() { }

    public Guid Id { get; private set; }

    public Guid ConfirmationId { get; private set; }

    public RequirementExperienceConfigurationConfirmation Confirmation { get; private set; } =
        null!;

    public Guid TechnicalProposalItemId { get; private set; }

    public RequirementTechnicalProposalItem TechnicalProposalItem { get; private set; } =
        null!;

    public bool IsIncluded { get; private set; }

    public int? Quantity { get; private set; }

    public int? WidthMillimeters { get; private set; }

    public int? HeightMillimeters { get; private set; }

    public decimal? AreaSquareMeters { get; private set; }

    public string? Location { get; private set; }

    public Guid? SystemId { get; private set; }

    public Guid? GlassTypeId { get; private set; }

    public Guid? FinishTypeId { get; private set; }

    public string? SystemState { get; private set; }

    public string? GlassState { get; private set; }

    public string? FinishState { get; private set; }

    public string? SystemOverrideState { get; private set; }

    public string? GlassOverrideState { get; private set; }

    public string[] ReviewReasons { get; private set; } = [];

    public string[] BlockingReasons { get; private set; } = [];

    public static RequirementExperienceConfigurationConfirmationItem Create(
        Guid confirmationId,
        Guid technicalProposalItemId,
        bool isIncluded,
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters,
        decimal? areaSquareMeters,
        string? location,
        Guid? systemId,
        Guid? glassTypeId,
        Guid? finishTypeId,
        string? systemState,
        string? glassState,
        string? finishState,
        string? systemOverrideState,
        string? glassOverrideState,
        IReadOnlyList<string> reviewReasons,
        IReadOnlyList<string> blockingReasons)
    {
        EnsureRequired(confirmationId, nameof(confirmationId));
        EnsureRequired(technicalProposalItemId, nameof(technicalProposalItemId));
        EnsurePositive(quantity, nameof(quantity));
        EnsurePositive(widthMillimeters, nameof(widthMillimeters));
        EnsurePositive(heightMillimeters, nameof(heightMillimeters));
        EnsureArea(areaSquareMeters, nameof(areaSquareMeters));

        return new RequirementExperienceConfigurationConfirmationItem
        {
            Id = Guid.NewGuid(),
            ConfirmationId = confirmationId,
            TechnicalProposalItemId = technicalProposalItemId,
            IsIncluded = isIncluded,
            Quantity = isIncluded ? quantity : null,
            WidthMillimeters = isIncluded ? widthMillimeters : null,
            HeightMillimeters = isIncluded ? heightMillimeters : null,
            AreaSquareMeters = isIncluded ? areaSquareMeters : null,
            Location = isIncluded
                ? RequirementExperienceConfigurationConfirmation
                    .NormalizeOptional(location, 500)
                : null,
            SystemId = isIncluded ? EmptyToNull(systemId) : null,
            GlassTypeId = isIncluded ? EmptyToNull(glassTypeId) : null,
            FinishTypeId = isIncluded ? EmptyToNull(finishTypeId) : null,
            SystemState = isIncluded
                ? NormalizeState(systemState, nameof(systemState))
                : null,
            GlassState = isIncluded
                ? NormalizeState(glassState, nameof(glassState))
                : null,
            FinishState = isIncluded
                ? NormalizeState(finishState, nameof(finishState))
                : null,
            SystemOverrideState = isIncluded
                ? NormalizeState(systemOverrideState, nameof(systemOverrideState))
                : null,
            GlassOverrideState = isIncluded
                ? NormalizeState(glassOverrideState, nameof(glassOverrideState))
                : null,
            ReviewReasons = isIncluded
                ? RequirementExperienceConfigurationConfirmation
                    .NormalizeReasons(reviewReasons)
                : [],
            BlockingReasons = isIncluded
                ? RequirementExperienceConfigurationConfirmation
                    .NormalizeReasons(blockingReasons)
                : []
        };
    }

    private static string? NormalizeState(string? value, string parameterName) =>
        value is null
            ? null
            : RequirementExperienceConfigurationConfirmation.NormalizeRequired(
                value,
                parameterName,
                64);

    private static Guid? EmptyToNull(Guid? value) =>
        value == Guid.Empty ? null : value;

    private static void EnsurePositive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentException(
                "El valor debe ser positivo.",
                parameterName);
        }
    }

    private static void EnsureArea(decimal? value, string parameterName)
    {
        if (value is <= 0m)
        {
            throw new ArgumentException(
                "El area debe ser positiva.",
                parameterName);
        }
    }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador es obligatorio.",
                parameterName);
        }
    }
}
