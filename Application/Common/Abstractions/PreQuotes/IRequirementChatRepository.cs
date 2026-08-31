using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementChatRepository
{
    Task<RequirementChatThread?> FindThreadAsync(
        Guid requirementId,
        RequirementChatScope scope,
        Guid? technicalProposalItemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RequirementChatMessage>> ListMessagesAsync(
        Guid chatThreadId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RequirementChatMessage>> ListRecentMessagesAsync(
        Guid chatThreadId,
        int limit,
        CancellationToken cancellationToken);

    Task<int> GetNextSequenceAsync(
        Guid chatThreadId,
        CancellationToken cancellationToken);

    void AddThread(RequirementChatThread thread);

    void AddMessage(RequirementChatMessage message);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class RequirementChatQueryException : Exception
{
    public RequirementChatQueryException(Exception innerException)
        : base("No fue posible consultar el chat del requerimiento.", innerException)
    {
    }
}

public sealed class RequirementChatPersistenceException : Exception
{
    public RequirementChatPersistenceException(Exception innerException)
        : base("No fue posible guardar el chat del requerimiento.", innerException)
    {
    }
}
