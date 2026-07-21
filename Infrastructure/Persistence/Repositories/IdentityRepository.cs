using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class IdentityRepository(ApplicationDbContext dbContext)
    : IIdentityRepository
{
    public Task<ExternalIdentity?> FindExternalIdentityForUpdateAsync(
        ExternalIdentityProvider provider,
        string providerSubject,
        CancellationToken cancellationToken)
    {
        return dbContext.ExternalIdentities
            .Include(identity => identity.User)
            .SingleOrDefaultAsync(
                identity => identity.Provider == provider
                    && identity.ProviderSubject == providerSubject,
                cancellationToken);
    }

    public Task<User?> FindUserByNormalizedEmailForUpdateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        return dbContext.Users.SingleOrDefaultAsync(
            user => user.Email == normalizedEmail,
            cancellationToken);
    }

    public Task<User?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == userId,
                cancellationToken);
    }

    public void Add(User user)
    {
        dbContext.Users.Add(user);
    }

    public void Add(ExternalIdentity externalIdentity)
    {
        dbContext.ExternalIdentities.Add(externalIdentity);
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
            throw new IdentityConflictException(exception);
        }
        catch (DbUpdateException exception)
        {
            throw new IdentityPersistenceException(exception);
        }
    }
}
