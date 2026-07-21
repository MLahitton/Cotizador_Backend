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
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Location { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Client Client { get; private set; } = null!;

    public User CreatedByUser { get; private set; } = null!;

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

    public void Activate(DateTimeOffset updatedAtUtc)
    {
        IsActive = true;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Deactivate(DateTimeOffset updatedAtUtc)
    {
        IsActive = false;
        UpdatedAtUtc = updatedAtUtc;
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