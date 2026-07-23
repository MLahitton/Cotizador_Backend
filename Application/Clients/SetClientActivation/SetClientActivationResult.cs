using Domain.Clients;

namespace Application.Clients.SetClientActivation;

public enum SetClientActivationFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5,
    PersistenceError = 6
}

public sealed record ClientActivationResult(
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

public sealed record SetClientActivationResult(
    SetClientActivationFailure Failure,
    ClientActivationResult? Client)
{
    public bool IsSuccess =>
        Failure == SetClientActivationFailure.None;

    public static SetClientActivationResult Success(
        ClientActivationResult client)
    {
        return new SetClientActivationResult(
            SetClientActivationFailure.None,
            client);
    }

    public static SetClientActivationResult Failed(
        SetClientActivationFailure failure)
    {
        return new SetClientActivationResult(failure, null);
    }
}
