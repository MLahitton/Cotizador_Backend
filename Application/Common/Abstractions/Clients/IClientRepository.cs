using Domain.Clients;

namespace Application.Common.Abstractions.Clients;

public interface IClientRepository
{
    Task<bool> ExistsByDocumentAsync(
        ClientDocumentType documentType,
        string documentNumber,
        CancellationToken cancellationToken);

    void Add(Client client);

    Task SaveChangesAsync(CancellationToken cancellationToken);
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
