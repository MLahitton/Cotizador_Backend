using Domain.Identity;

namespace Domain.Clients;

public sealed class Client
{
    private Client()
    {
    }

    private Client(
        Guid id,
        ClientType clientType,
        string legalName,
        string? tradeName,
        ClientDocumentType? documentType,
        string? documentNumber,
        string? email,
        string? phone,
        string? address,
        string? city,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ClientType = clientType;
        LegalName = NormalizeRequired(legalName, nameof(legalName));
        TradeName = NormalizeOptional(tradeName);
        DocumentType = documentType;
        DocumentNumber = NormalizeOptional(documentNumber);
        Email = NormalizeOptional(email)?.ToLowerInvariant();
        Phone = NormalizeOptional(phone);
        Address = NormalizeOptional(address);
        City = NormalizeOptional(city);
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        StatusChangedByUserId = null;
        StatusChangedAtUtc = null;
    }

    public Guid Id { get; private set; }

    public ClientType ClientType { get; private set; }

    public string LegalName { get; private set; } = string.Empty;

    public string? TradeName { get; private set; }

    public ClientDocumentType? DocumentType { get; private set; }

    public string? DocumentNumber { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public Guid? StatusChangedByUserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? StatusChangedAtUtc { get; private set; }

    public User CreatedByUser { get; private set; } = null!;

    public User UpdatedByUser { get; private set; } = null!;

    public User? StatusChangedByUser { get; private set; }

    public static Client Create(
        ClientType clientType,
        string legalName,
        string? tradeName,
        ClientDocumentType? documentType,
        string? documentNumber,
        string? email,
        string? phone,
        string? address,
        string? city,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario creador es obligatorio.",
                nameof(createdByUserId));
        }

        return new Client(
            Guid.NewGuid(),
            clientType,
            legalName,
            tradeName,
            documentType,
            documentNumber,
            email,
            phone,
            address,
            city,
            createdByUserId,
            createdAtUtc);
    }

    public void UpdateDetails(
        ClientType clientType,
        string legalName,
        string? tradeName,
        ClientDocumentType? documentType,
        string? documentNumber,
        string? email,
        string? phone,
        string? address,
        string? city,
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

        ClientType = clientType;
        LegalName = NormalizeRequired(legalName, nameof(legalName));
        TradeName = NormalizeOptional(tradeName);
        DocumentType = documentType;
        DocumentNumber = NormalizeOptional(documentNumber);
        Email = NormalizeOptional(email)?.ToLowerInvariant();
        Phone = NormalizeOptional(phone);
        Address = NormalizeOptional(address);
        City = NormalizeOptional(city);
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
