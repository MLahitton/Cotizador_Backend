namespace Application.Clients.CreateClient;

public sealed record CreateClientCommand(
    string? ClientType,
    string? LegalName,
    string? TradeName,
    string? DocumentType,
    string? DocumentNumber,
    string? Email,
    string? Phone,
    string? Address,
    string? City);
