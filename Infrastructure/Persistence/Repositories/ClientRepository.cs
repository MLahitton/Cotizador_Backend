using Application.Common.Abstractions.Clients;
using Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ClientRepository(ApplicationDbContext dbContext)
    : IClientRepository
{
    public Task<bool> ExistsByDocumentAsync(
        ClientDocumentType documentType,
        string documentNumber,
        CancellationToken cancellationToken)
    {
        return dbContext.Clients
            .AsNoTracking()
            .AnyAsync(
                client => client.DocumentType == documentType
                    && client.DocumentNumber == documentNumber,
                cancellationToken);
    }

    public void Add(Client client)
    {
        dbContext.Clients.Add(client);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            throw new ClientConflictException(exception);
        }
        catch (DbUpdateException exception)
        {
            throw new ClientPersistenceException(exception);
        }
    }
}
