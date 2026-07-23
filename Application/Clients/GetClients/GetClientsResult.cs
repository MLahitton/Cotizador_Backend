using Domain.Clients;

namespace Application.Clients.GetClients;

public enum GetClientsFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    QueryError = 4
}

public sealed record ClientListItemResult(
    Guid Id,
    ClientType ClientType,
    string LegalName,
    string? TradeName,
    ClientDocumentType? DocumentType,
    string? DocumentNumber,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ClientsPageResult(
    IReadOnlyList<ClientListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetClientsResult(
    GetClientsFailure Failure,
    ClientsPageResult? Page)
{
    public bool IsSuccess => Failure == GetClientsFailure.None;

    public static GetClientsResult Success(ClientsPageResult page)
        => new(GetClientsFailure.None, page);

    public static GetClientsResult Failed(GetClientsFailure failure)
        => new(failure, null);
}
