using Application.Common.Abstractions.Administration;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Queries;

public sealed class AdministrationPreQuoteReader(
    ApplicationDbContext dbContext)
    : IAdministrationPreQuoteReader
{
    public async Task<AdministrationPreQuoteSearchPage> SearchAsync(
        AdministrationPreQuoteSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.PreQuotes
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(criteria.Search))
            {
                var search = criteria.Search.Trim();

                query = query.Where(preQuote =>
                    EF.Functions.ILike(
                        preQuote.Serial,
                        $"%{search}%")
                    || preQuote.Name != null
                    && EF.Functions.ILike(
                        preQuote.Name,
                        $"%{search}%")
                    || EF.Functions.ILike(
                        preQuote.Project.Code,
                        $"%{search}%")
                    || EF.Functions.ILike(
                        preQuote.Project.Name,
                        $"%{search}%"));
            }

            if (criteria.UserId is Guid userId)
            {
                query = query.Where(
                    preQuote => preQuote.CreatedByUserId == userId);
            }

            if (criteria.FromUtc is not null)
            {
                query = query.Where(
                    preQuote =>
                        preQuote.CreatedAtUtc >= criteria.FromUtc);
            }

            if (criteria.ToUtc is not null)
            {
                query = query.Where(
                    preQuote =>
                        preQuote.CreatedAtUtc <= criteria.ToUtc);
            }

            var totalCount = await query.CountAsync(
                cancellationToken);

            var items = await query
                .OrderByDescending(preQuote => preQuote.UpdatedAtUtc)
                .ThenByDescending(preQuote => preQuote.CreatedAtUtc)
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .Select(preQuote => new
                {
                    PreQuote = preQuote,

                    DocumentCount =
                        dbContext.PreQuoteDocuments.Count(
                            document =>
                                document.PreQuoteId == preQuote.Id),

                    LatestRequirement = dbContext.Requirements
                        .Where(requirement =>
                            requirement.PreQuoteId == preQuote.Id)
                        .OrderByDescending(requirement =>
                            requirement.CreatedAtUtc)
                        .ThenByDescending(requirement =>
                            requirement.Id)
                        .Select(requirement => new
                        {
                            requirement.Id,
                            Status = requirement.Status.ToString()
                        })
                        .FirstOrDefault(),

                    LatestTechnicalProposal =
                        dbContext.RequirementTechnicalProposals
                            .Where(proposal =>
                                dbContext.Requirements.Any(
                                    requirement =>
                                        requirement.Id
                                            == proposal.RequirementId
                                        && requirement.PreQuoteId
                                            == preQuote.Id))
                            .OrderByDescending(proposal =>
                                proposal.CreatedAtUtc)
                            .ThenByDescending(proposal =>
                                proposal.Id)
                            .Select(proposal => new
                            {
                                proposal.Id,
                                ItemCount =
                                    dbContext
                                        .RequirementTechnicalProposalItems
                                        .Count(item =>
                                            item.TechnicalProposalId
                                                == proposal.Id)
                            })
                            .FirstOrDefault(),

                    LatestAttempt =
                        dbContext.DocumentProcessingAttempts
                            .Where(attempt =>
                                dbContext.PreQuoteDocuments.Any(
                                    document =>
                                        document.Id
                                            == attempt.PreQuoteDocumentId
                                        && document.PreQuoteId
                                            == preQuote.Id))
                            .OrderByDescending(attempt =>
                                attempt.CreatedAtUtc)
                            .ThenByDescending(attempt =>
                                attempt.Id)
                            .Select(attempt => new
                            {
                                State =
                                    attempt.ProcessingState.ToString(),
                                Outcome =
                                    attempt.Outcome == null
                                        ? null
                                        : attempt.Outcome.ToString(),
                                attempt.ErrorCode
                            })
                            .FirstOrDefault()
                })
                .Select(item => new AdministrationPreQuoteSearchItem(
                    item.PreQuote.Id,
                    item.PreQuote.ProjectId,
                    item.PreQuote.Serial,
                    item.PreQuote.Name,
                    item.DocumentCount,
                    item.PreQuote.CreatedAtUtc,
                    item.PreQuote.UpdatedAtUtc,
                    new AdministrationPreQuoteUser(
                        item.PreQuote.CreatedByUser.Id,
                        item.PreQuote.CreatedByUser.Email,
                        item.PreQuote.CreatedByUser.FirstName,
                        item.PreQuote.CreatedByUser.LastName),
                    new AdministrationPreQuoteProject(
                        item.PreQuote.Project.Id,
                        item.PreQuote.Project.Code,
                        item.PreQuote.Project.Name),
                    item.LatestRequirement != null,
                    item.LatestRequirement == null
                        ? null
                        : item.LatestRequirement.Id,
                    item.LatestRequirement == null
                        ? null
                        : item.LatestRequirement.Status,
                    item.LatestTechnicalProposal != null,
                    item.LatestTechnicalProposal == null
                        ? null
                        : item.LatestTechnicalProposal.Id,
                    item.LatestTechnicalProposal == null
                        ? null
                        : item.LatestTechnicalProposal.ItemCount,
                    item.LatestAttempt == null
                        ? null
                        : item.LatestAttempt.State,
                    item.LatestAttempt == null
                        ? null
                        : item.LatestAttempt.Outcome,
                    item.LatestAttempt == null
                        ? null
                        : item.LatestAttempt.ErrorCode))
                .ToArrayAsync(cancellationToken);

            return new AdministrationPreQuoteSearchPage(
                items,
                totalCount);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            throw new AdministrationPreQuoteQueryException(
                exception);
        }
    }
}