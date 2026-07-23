namespace Contracts.Clients;

public sealed record CreateClientResponse(
    Guid Id,
    string ClientType,
    string LegalName,
    string? TradeName,
    string? DocumentType,
    string? DocumentNumber,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
