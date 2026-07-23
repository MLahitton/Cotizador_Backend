using Domain.Clients;
using Domain.Identity;

namespace Domain.Projects;

public sealed class Project
{
    private Project()
    {
    }

    private Project(
        Guid id,
        Guid clientId,
        string code,
        string name,
        string? description,
        string? location,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ClientId = clientId;
        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name));
        Description = NormalizeOptional(description);
        Location = NormalizeOptional(location);
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        StatusChangedByUserId = null;
        StatusChangedAtUtc = null;
    }

    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Location { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public Guid? StatusChangedByUserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? StatusChangedAtUtc { get; private set; }

    public Client Client { get; private set; } = null!;

    public User CreatedByUser { get; private set; } = null!;

    public User UpdatedByUser { get; private set; } = null!;

    public User? StatusChangedByUser { get; private set; }

    public static Project Create(
        Guid clientId,
        string code,
        string name,
        string? description,
        string? location,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException(
                "El cliente es obligatorio.",
                nameof(clientId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario creador es obligatorio.",
                nameof(createdByUserId));
        }

        return new Project(
            Guid.NewGuid(),
            clientId,
            code,
            name,
            description,
            location,
            createdByUserId,
            createdAtUtc);
    }

    public void UpdateDetails(
        string code,
        string name,
        string? description,
        string? location,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (updatedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario que modifica es obligatorio.",
                nameof(updatedByUserId));
        }

        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualización no puede ser anterior a la última actualización.",
                nameof(updatedAtUtc));
        }

        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name));
        Description = NormalizeOptional(description);
        Location = NormalizeOptional(location);
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SetActive(
        bool isActive,
        Guid changedByUserId,
        DateTimeOffset changedAtUtc)
    {
        if (changedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario que cambia el estado es obligatorio.",
                nameof(changedByUserId));
        }

        if (changedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de cambio de estado no puede ser anterior a la última actualización.",
                nameof(changedAtUtc));
        }

        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        UpdatedByUserId = changedByUserId;
        UpdatedAtUtc = changedAtUtc;
        StatusChangedByUserId = changedByUserId;
        StatusChangedAtUtc = changedAtUtc;
    }

    private static string NormalizeCode(string value)
    {
        return NormalizeRequired(value, nameof(value)).ToUpperInvariant();
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "El valor es obligatorio.",
                parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
