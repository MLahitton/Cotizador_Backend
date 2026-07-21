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
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
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

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

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
        DateTimeOffset createdAtUtc)
    {
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