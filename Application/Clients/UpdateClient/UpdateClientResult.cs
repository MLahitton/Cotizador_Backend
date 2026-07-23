using Domain.Clients;

namespace Application.Clients.UpdateClient;

public enum UpdateClientFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    DuplicateDocument = 5,
    QueryError = 6,
    PersistenceError = 7
}

public sealed record UpdatedClientResult(
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

public sealed record UpdateClientResult(
    UpdateClientFailure Failure,
    UpdatedClientResult? Client)
{
    public bool IsSuccess => Failure == UpdateClientFailure.None;

    public static UpdateClientResult Success(UpdatedClientResult client)
    {
        return new UpdateClientResult(
            UpdateClientFailure.None,
            client);
    }

    public static UpdateClientResult Failed(
        UpdateClientFailure failure)
    {
        return new UpdateClientResult(failure, null);
    }
}
