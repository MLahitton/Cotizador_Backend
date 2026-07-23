using Domain.Clients;

namespace Application.Common.Abstractions.Clients;

public interface IClientRepository
{
    Task<Client?> FindByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken);

    Task<Client?> FindForUpdateByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken);

    Task<ClientSearchPage> SearchAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<bool> ExistsByDocumentAsync(
        ClientDocumentType documentType,
        string documentNumber,
        CancellationToken cancellationToken);

    Task<bool> ExistsByDocumentForOtherClientAsync(
        Guid clientId,
        ClientDocumentType documentType,
        string documentNumber,
        CancellationToken cancellationToken);

    void Add(Client client);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ClientSearchPage(
    IReadOnlyList<Client> Items,
    int TotalCount);

public sealed class ClientQueryException : Exception
{
    public ClientQueryException(Exception innerException)
        : base(
            "No fue posible consultar los clientes.",
            innerException)
    {
    }
}

public sealed class ClientConflictException : Exception
{
    public ClientConflictException(Exception innerException)
        : base(
            "Se detecto un conflicto al guardar el cliente.",
            innerException)
    {
    }
}

public sealed class ClientPersistenceException : Exception
{
    public ClientPersistenceException(Exception innerException)
        : base(
            "No fue posible guardar el cliente.",
            innerException)
    {
    }
}
