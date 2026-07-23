namespace Contracts.Clients;

public sealed record UpdateClientRequest(
    string? ClientType,
    string? LegalName,
    string? TradeName,
    string? DocumentType,
    string? DocumentNumber,
    string? Email,
    string? Phone,
    string? Address,
    string? City);
