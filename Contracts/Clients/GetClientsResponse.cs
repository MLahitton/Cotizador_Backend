namespace Contracts.Clients;

public sealed record GetClientsResponse(
    IReadOnlyList<ClientListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
