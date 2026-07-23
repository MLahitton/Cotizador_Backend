namespace Application.Clients.GetClients;

public sealed record GetClientsQuery(
    string? Search,
    string? Status,
    int Page,
    int PageSize);
