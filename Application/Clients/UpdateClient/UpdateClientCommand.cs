namespace Application.Clients.UpdateClient;

public sealed record UpdateClientCommand(
    Guid ClientId,
    string? ClientType,
    string? LegalName,
    string? TradeName,
    string? DocumentType,
    string? DocumentNumber,
    string? Email,
    string? Phone,
    string? Address,
    string? City);
