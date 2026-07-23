using Domain.Clients;

namespace Application.Clients.CreateClient;

public enum CreateClientFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    DuplicateDocument = 4,
    PersistenceError = 5
}

public sealed record CreatedClientResult(
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

public sealed record CreateClientResult(
    CreateClientFailure Failure,
    CreatedClientResult? Client)
{
    public bool IsSuccess => Failure == CreateClientFailure.None;

    public static CreateClientResult Success(CreatedClientResult client)
    {
        return new CreateClientResult(
            CreateClientFailure.None,
            client);
    }

    public static CreateClientResult Failed(CreateClientFailure failure)
    {
        return new CreateClientResult(failure, null);
    }
}
