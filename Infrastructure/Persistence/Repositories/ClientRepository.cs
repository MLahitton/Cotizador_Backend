using System.Data.Common;
using Application.Common.Abstractions.Clients;
using Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ClientRepository(ApplicationDbContext dbContext)
    : IClientRepository
{
    public async Task<Client?> FindByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Clients
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    client => client.Id == clientId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ClientQueryException(exception);
        }
    }

    public async Task<Client?> FindForUpdateByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Clients
                .SingleOrDefaultAsync(
                    client => client.Id == clientId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ClientQueryException(exception);
        }
    }

    public async Task<ClientSearchPage> SearchAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Clients
                .AsNoTracking()
                .AsQueryable();

            if (isActive is bool activeState)
            {
                query = query.Where(
                    client => client.IsActive == activeState);
            }

            if (search is not null)
            {
                var escapedSearch = EscapeLikePattern(search);
                var pattern = $"%{escapedSearch}%";

                query = query.Where(client =>
                    EF.Functions.ILike(
                        client.LegalName,
                        pattern,
                        "\\")
                    || (client.TradeName != null
                        && EF.Functions.ILike(
                            client.TradeName,
                            pattern,
                            "\\"))
                    || (client.DocumentNumber != null
                        && EF.Functions.ILike(
                            client.DocumentNumber,
                            pattern,
                            "\\"))
                    || (client.Email != null
                        && EF.Functions.ILike(
                            client.Email,
                            pattern,
                            "\\")));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)page - 1L) * pageSize;

            if (totalCount == 0
                || skip >= totalCount
                || skip > int.MaxValue)
            {
                return new ClientSearchPage(
                    Array.Empty<Client>(),
                    totalCount);
            }

            var items = await query
                .OrderBy(client => client.LegalName)
                .ThenBy(client => client.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new ClientSearchPage(items, totalCount);
        }
        catch (DbException exception)
        {
            throw new ClientQueryException(exception);
        }
    }

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

    public async Task<bool> ExistsByDocumentForOtherClientAsync(
        Guid clientId,
        ClientDocumentType documentType,
        string documentNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Clients
                .AsNoTracking()
                .AnyAsync(
                    client => client.Id != clientId
                        && client.DocumentType == documentType
                        && client.DocumentNumber == documentNumber,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ClientQueryException(exception);
        }
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

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
