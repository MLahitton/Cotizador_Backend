using Domain.Clients;

namespace Application.Clients.GetClientById;

public enum GetClientByIdFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5
}

public sealed record ClientByIdResult(
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

public sealed record GetClientByIdResult(
    GetClientByIdFailure Failure,
    ClientByIdResult? Client)
{
    public bool IsSuccess => Failure == GetClientByIdFailure.None;

    public static GetClientByIdResult Success(ClientByIdResult client)
    {
        return new GetClientByIdResult(
            GetClientByIdFailure.None,
            client);
    }

    public static GetClientByIdResult Failed(
        GetClientByIdFailure failure)
    {
        return new GetClientByIdResult(failure, null);
    }
}
