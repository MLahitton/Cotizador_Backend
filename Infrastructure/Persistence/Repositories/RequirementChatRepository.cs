using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class RequirementChatRepository(ApplicationDbContext dbContext)
    : IRequirementChatRepository
{
    public async Task<RequirementChatThread?> FindThreadAsync(
        Guid requirementId,
        RequirementChatScope scope,
        Guid? technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementChatThreads
                .SingleOrDefaultAsync(thread =>
                    thread.RequirementId == requirementId
                    && thread.Scope == scope
                    && thread.TechnicalProposalItemId == technicalProposalItemId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementChatQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<RequirementChatMessage>> ListMessagesAsync(
        Guid chatThreadId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementChatMessages
                .AsNoTracking()
                .Where(message => message.ChatThreadId == chatThreadId)
                .OrderBy(message => message.Sequence)
                .ThenBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Id)
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementChatQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<RequirementChatMessage>> ListRecentMessagesAsync(
        Guid chatThreadId,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementChatMessages
                .AsNoTracking()
                .Where(message => message.ChatThreadId == chatThreadId)
                .OrderByDescending(message => message.Sequence)
                .Take(limit)
                .OrderBy(message => message.Sequence)
                .ThenBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Id)
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementChatQueryException(exception);
        }
    }

    public async Task<int> GetNextSequenceAsync(
        Guid chatThreadId,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await dbContext.RequirementChatMessages
                .Where(message => message.ChatThreadId == chatThreadId)
                .MaxAsync(
                    message => (int?)message.Sequence,
                    cancellationToken);
            return (current ?? 0) + 1;
        }
        catch (DbException exception)
        {
            throw new RequirementChatQueryException(exception);
        }
    }

    public void AddThread(RequirementChatThread thread)
    {
        dbContext.RequirementChatThreads.Add(thread);
    }

    public void AddMessage(RequirementChatMessage message)
    {
        dbContext.RequirementChatMessages.Add(message);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new RequirementChatPersistenceException(exception);
        }
    }
}
