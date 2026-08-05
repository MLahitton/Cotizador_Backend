using System.Data.Common;
using Application.Common.Abstractions.Catalogs;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class GlassTypeCatalogRepository(ApplicationDbContext dbContext)
    : IGlassTypeCatalogRepository
{
    public async Task<IReadOnlyList<GlassTypeCatalogReadModel>>
        GetActiveWithCurrentPriceRangesAsync(
            CancellationToken cancellationToken)
    {
        try
        {
            return await Query()
                .ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new GlassTypeCatalogQueryException(exception);
        }
    }

    public async Task<GlassTypeCatalogReadModel?>
        GetActiveByCodeWithCurrentPriceRangeAsync(
            string normalizedCode,
            CancellationToken cancellationToken)
    {
        try
        {
            return await Query().SingleOrDefaultAsync(
                value => value.Code == normalizedCode,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new GlassTypeCatalogQueryException(exception);
        }
    }

    private IQueryable<GlassTypeCatalogReadModel> Query() =>
        dbContext.GlassTypes
            .AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Code)
            .Select(value => new GlassTypeCatalogReadModel(
                value.Id,
                value.Code,
                value.Name,
                value.Description,
                value.IsActive,
                value.PriceRangeVersions
                    .Where(range => range.ValidToUtc == null)
                    .Select(range => new GlassPriceRangeCatalogReadModel(
                        range.Id,
                        range.Version,
                        range.MinimumPricePerSquareMeter,
                        range.ExpectedAmountPerM2,
                        range.MaximumPricePerSquareMeter,
                        range.Currency,
                        range.Status,
                        range.ValidFromUtc,
                        range.ValidToUtc))
                    .SingleOrDefault()));
}
