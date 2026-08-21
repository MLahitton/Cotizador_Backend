using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteRepository(ApplicationDbContext dbContext)
    : IPreQuoteRepository
{
    public async Task<PreQuote?> FindForUpdateByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.PreQuotes.SingleOrDefaultAsync(
                preQuote => preQuote.Id == preQuoteId,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteQueryException(exception);
        }
    }

    public async Task<PreQuoteDetails?> FindByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.PreQuotes
                .AsNoTracking()
                .Where(preQuote => preQuote.Id == preQuoteId)
                .Select(preQuote => new PreQuoteDetails(
                    preQuote.Id,
                    preQuote.ProjectId,
                    dbContext.PreQuoteDocuments.Count(document =>
                        document.PreQuoteId == preQuote.Id),
                    preQuote.CreatedAtUtc,
                    preQuote.UpdatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteQueryException(exception);
        }
    }

    public async Task<PreQuoteSearchPage> SearchByProjectAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.PreQuotes
                .AsNoTracking()
                .Where(preQuote => preQuote.ProjectId == projectId);

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)page - 1L) * pageSize;

            if (totalCount == 0
                || skip >= totalCount
                || skip > int.MaxValue)
            {
                return new PreQuoteSearchPage(
                    Array.Empty<PreQuoteSearchItem>(),
                    totalCount);
            }

            var items = await query
                .OrderByDescending(
                    preQuote => preQuote.CreatedAtUtc)
                .ThenByDescending(
                    preQuote => preQuote.UpdatedAtUtc)
                .ThenByDescending(preQuote => preQuote.Id)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(preQuote => new PreQuotePageProjection(
                    preQuote.Id,
                    preQuote.ProjectId,
                    dbContext.PreQuoteDocuments.Count(document =>
                        document.PreQuoteId == preQuote.Id),
                    preQuote.CreatedAtUtc,
                    preQuote.UpdatedAtUtc))
                .ToListAsync(cancellationToken);

            var preQuoteIds = items
                .Select(preQuote => preQuote.Id)
                .ToArray();

            var requirements = await dbContext.Requirements
                .AsNoTracking()
                .Where(requirement =>
                    preQuoteIds.Contains(requirement.PreQuoteId)
                    && requirement.IsActive)
                .Select(requirement => new PreQuoteRequirementProjection(
                    requirement.Id,
                    requirement.PreQuoteId,
                    requirement.Status,
                    requirement.CreatedAtUtc,
                    dbContext.RequirementTechnicalProposals.Any(proposal =>
                        proposal.RequirementId == requirement.Id),
                    dbContext.RequirementTechnicalProposals
                        .Where(proposal =>
                            proposal.RequirementId == requirement.Id)
                        .OrderByDescending(proposal =>
                            proposal.ProcessingAttempt.CompletedAtUtc)
                        .ThenByDescending(proposal => proposal.CreatedAtUtc)
                        .ThenByDescending(proposal => proposal.Id)
                        .Select(proposal => (Guid?)proposal.Id)
                        .FirstOrDefault(),
                    dbContext.RequirementTechnicalProposals
                        .Where(proposal =>
                            proposal.RequirementId == requirement.Id)
                        .OrderByDescending(proposal =>
                            proposal.ProcessingAttempt.CompletedAtUtc)
                        .ThenByDescending(proposal => proposal.CreatedAtUtc)
                        .ThenByDescending(proposal => proposal.Id)
                        .Select(proposal => (int?)proposal.Items.Count)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt =>
                            (DocumentProcessingState?)attempt.ProcessingState)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => attempt.Outcome)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => attempt.ErrorCode)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);

            var currentRequirements = requirements
                .GroupBy(requirement => requirement.PreQuoteId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(requirement => requirement.Rank)
                        .ThenByDescending(requirement =>
                            requirement.CreatedAtUtc)
                        .First());

            var enrichedItems = items
                .Select(preQuote =>
                {
                    currentRequirements.TryGetValue(
                        preQuote.Id,
                        out var requirement);

                    return new PreQuoteSearchItem(
                        preQuote.Id,
                        preQuote.ProjectId,
                        preQuote.DocumentCount,
                        preQuote.CreatedAtUtc,
                        preQuote.UpdatedAtUtc,
                        requirement is not null,
                        requirement?.RequirementId,
                        requirement?.Status,
                        requirement?.HasTechnicalProposal ?? false,
                        requirement?.TechnicalProposalId,
                        requirement?.TechnicalProposalItemCount,
                        requirement?.LatestAttemptState,
                        requirement?.LatestAttemptOutcome,
                        requirement?.LatestAttemptErrorCode);
                })
                .ToArray();

            return new PreQuoteSearchPage(enrichedItems, totalCount);
        }
        catch (DbException exception)
        {
            throw new PreQuoteQueryException(exception);
        }
    }

    public void Add(PreQuote preQuote)
    {
        dbContext.PreQuotes.Add(preQuote);
    }

    public void AddDocument(PreQuoteDocument document)
    {
        dbContext.PreQuoteDocuments.Add(document);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new PreQuotePersistenceException(exception);
        }
    }

    private sealed record PreQuotePageProjection(
        Guid Id,
        Guid ProjectId,
        int DocumentCount,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record PreQuoteRequirementProjection(
        Guid RequirementId,
        Guid PreQuoteId,
        RequirementStatus Status,
        DateTimeOffset CreatedAtUtc,
        bool HasTechnicalProposal,
        Guid? TechnicalProposalId,
        int? TechnicalProposalItemCount,
        DocumentProcessingState? LatestAttemptState,
        DocumentProcessingOutcome? LatestAttemptOutcome,
        string? LatestAttemptErrorCode)
    {
        public int Rank =>
            HasTechnicalProposal
                ? 1
                : Status == RequirementStatus.Processing
                    || LatestAttemptState == DocumentProcessingState.Processing
                    ? 2
                    : Status == RequirementStatus.Pending
                        ? 3
                        : 4;
    }
}
