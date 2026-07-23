namespace Application.Clients.GetClients;

public sealed record GetClientsQuery(
    string? Search,
    int Page,
    int PageSize);
