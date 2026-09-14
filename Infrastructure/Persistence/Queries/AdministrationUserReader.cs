using Application.Common.Abstractions.Administration;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Queries;

public sealed class AdministrationUserReader(
    ApplicationDbContext dbContext)
    : IAdministrationUserReader
{
    public async Task<AdministrationUserSearchPage> SearchAsync(
        AdministrationUserSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Users
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(criteria.Search))
            {
                var search = criteria.Search.Trim();

                query = query.Where(user =>
                    EF.Functions.ILike(
                        user.Email,
                        $"%{search}%")
                    || EF.Functions.ILike(
                        user.FirstName,
                        $"%{search}%")
                    || user.LastName != null
                    && EF.Functions.ILike(
                        user.LastName,
                        $"%{search}%"));
            }

            if (criteria.IsActive is bool isActive)
            {
                query = query.Where(
                    user => user.IsActive == isActive);
            }

            if (criteria.Role is not null)
            {
                query = query.Where(
                    user => user.Role == criteria.Role);
            }

            var totalCount = await query.CountAsync(
                cancellationToken);

            var items = await query
                .OrderByDescending(user => user.LastLoginAtUtc)
                .ThenBy(user => user.Email)
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .Select(user => new AdministrationUserSearchItem(
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.ProfilePictureUrl,
                    user.IsActive,
                    user.Role,
                    user.LastLoginAtUtc,
                    user.CreatedAtUtc,
                    user.UpdatedAtUtc,
                    dbContext.PreQuotes.Count(
                        preQuote =>
                            preQuote.CreatedByUserId == user.Id)))
                .ToArrayAsync(cancellationToken);

            return new AdministrationUserSearchPage(
                items,
                totalCount);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            throw new AdministrationUserQueryException(
                exception);
        }
    }
}