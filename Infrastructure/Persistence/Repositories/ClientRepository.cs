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
        ClientSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Clients
                .AsNoTracking()
                .AsQueryable();

            if (criteria.IsActive is bool activeState)
            {
                query = query.Where(
                    client => client.IsActive == activeState);
            }

            if (criteria.ClientType is { } clientType)
            {
                query = query.Where(client =>
                    client.ClientType == clientType);
            }

            if (criteria.DocumentType is { } documentType)
            {
                query = query.Where(client =>
                    client.DocumentType == documentType);
            }

            if (criteria.NormalizedDocumentNumber is { } documentNumber)
            {
                query = query.Where(client =>
                    client.DocumentNumber != null
                    && client.DocumentNumber
                        .Replace(" ", "")
                        .Replace(".", "")
                        .Replace("-", "")
                        .Replace("/", "") == documentNumber);
            }

            if (criteria.Search is { } search)
            {
                var escapedSearch = EscapeLikePattern(search);
                var pattern = $"%{escapedSearch}%";
                var normalizedSearch = NormalizeDocumentNumber(search);
                var documentPattern =
                    $"%{EscapeLikePattern(normalizedSearch)}%";

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
                        && normalizedSearch.Length > 0
                        && EF.Functions.ILike(
                            client.DocumentNumber
                                .Replace(" ", "")
                                .Replace(".", "")
                                .Replace("-", "")
                                .Replace("/", ""),
                            documentPattern,
                            "\\"))
                    || (client.Email != null
                        && EF.Functions.ILike(
                            client.Email,
                            pattern,
                            "\\"))
                    || (client.Phone != null
                        && EF.Functions.ILike(
                            client.Phone,
                            pattern,
                            "\\"))
                    || (client.Address != null
                        && EF.Functions.ILike(
                            client.Address,
                            pattern,
                            "\\"))
                    || (client.City != null
                        && EF.Functions.ILike(
                            client.City,
                            pattern,
                            "\\")));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)criteria.Page - 1L)
                * criteria.PageSize;

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
                .Take(criteria.PageSize)
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

    private static string NormalizeDocumentNumber(string value)
    {
        return value
            .Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal);
    }
}
