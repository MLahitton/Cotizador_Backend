using Domain.Identity;

namespace Application.Common.Abstractions.Authentication;

public interface IIdentityRepository
{
    Task<ExternalIdentity?> FindExternalIdentityForUpdateAsync(
        ExternalIdentityProvider provider,
        string providerSubject,
        CancellationToken cancellationToken);

    Task<User?> FindUserByNormalizedEmailForUpdateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<User?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    void Add(User user);

    void Add(ExternalIdentity externalIdentity);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class IdentityConflictException : Exception
{
    public IdentityConflictException(Exception innerException)
        : base(
            "Se detecto un conflicto al guardar la identidad.",
            innerException)
    {
    }
}

public sealed class IdentityPersistenceException : Exception
{
    public IdentityPersistenceException(Exception innerException)
        : base(
            "No fue posible guardar la identidad.",
            innerException)
    {
    }
}
