namespace Domain.PreQuotes;

public enum RequirementChatScope
{
    Requirement = 1,
    Item = 2
}

public enum RequirementChatMessageRole
{
    User = 1,
    Assistant = 2
}

public sealed class RequirementChatThread
{
    private readonly List<RequirementChatMessage> _messages = [];

    private RequirementChatThread() { }

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid? TechnicalProposalItemId { get; private set; }
    public RequirementChatScope Scope { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Requirement Requirement { get; private set; } = null!;
    public IReadOnlyCollection<RequirementChatMessage> Messages => _messages;

    public static RequirementChatThread Create(
        Guid requirementId,
        RequirementChatScope scope,
        Guid? technicalProposalItemId,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (requirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento es obligatorio.",
                nameof(requirementId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario es obligatorio.",
                nameof(createdByUserId));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        if (scope == RequirementChatScope.Requirement
            && technicalProposalItemId is not null)
        {
            throw new ArgumentException(
                "El chat global no debe tener item asociado.",
                nameof(technicalProposalItemId));
        }

        if (scope == RequirementChatScope.Item
            && (technicalProposalItemId is null
                || technicalProposalItemId == Guid.Empty))
        {
            throw new ArgumentException(
                "El chat de item requiere un item asociado.",
                nameof(technicalProposalItemId));
        }

        return new RequirementChatThread
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            TechnicalProposalItemId = technicalProposalItemId,
            Scope = scope,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public void Touch(DateTimeOffset updatedAtUtc)
    {
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior.",
                nameof(updatedAtUtc));
        }

        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class RequirementChatMessage
{
    public const int MaximumContentLength = 4000;

    private RequirementChatMessage() { }

    public Guid Id { get; private set; }
    public Guid ChatThreadId { get; private set; }
    public RequirementChatMessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Sequence { get; private set; }
    public string? MetadataJson { get; private set; }
    public RequirementChatThread ChatThread { get; private set; } = null!;

    public static RequirementChatMessage Create(
        Guid chatThreadId,
        RequirementChatMessageRole role,
        string content,
        int sequence,
        DateTimeOffset createdAtUtc,
        string? metadataJson = null)
    {
        if (chatThreadId == Guid.Empty)
        {
            throw new ArgumentException(
                "El hilo de chat es obligatorio.",
                nameof(chatThreadId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentException(
                "La secuencia del mensaje debe ser mayor que cero.",
                nameof(sequence));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementChatMessage
        {
            Id = Guid.NewGuid(),
            ChatThreadId = chatThreadId,
            Role = role,
            Content = Requirement.NormalizeRequired(
                content,
                nameof(content),
                MaximumContentLength),
            Sequence = sequence,
            CreatedAtUtc = createdAtUtc,
            MetadataJson = NormalizeOptional(metadataJson, 8000)
        };
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                "El texto excede la longitud permitida.",
                nameof(value));
        }

        return normalized;
    }
}
