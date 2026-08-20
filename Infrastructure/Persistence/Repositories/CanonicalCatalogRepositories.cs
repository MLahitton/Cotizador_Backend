using System.Data.Common;
using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class ProductSystemCatalogRepository(
    ApplicationDbContext dbContext) : IProductSystemCatalogRepository
{
    public async Task<IReadOnlyList<ProductSystemCatalogReadModel>>
        ListActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Query().ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    public async Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Query().SingleOrDefaultAsync(
                value => value.Code == code,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<ProductSystemCatalogReadModel>>
        ListActiveSelectableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Query()
                .Where(value => value.IsSelectable)
                .ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    private IQueryable<ProductSystemCatalogReadModel> Query() =>
        dbContext.ProductSystems.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Code)
            .Select(value => new ProductSystemCatalogReadModel(
                value.Id,
                value.Code,
                value.Name,
                value.TechnicalName,
                value.CommercialName,
                value.FunctionalType,
                value.Family,
                value.Series,
                value.CommercialLine,
                value.Variant,
                value.IsSelectable,
                value.ActiveForRecognition,
                value.Priceable,
                value.FuturePriceable,
                value.RequiresReview,
                value.IsActive));
}

public sealed class FrameTypeCatalogRepository(
    ApplicationDbContext dbContext) : IFrameTypeCatalogRepository
{
    public async Task<IReadOnlyList<FrameTypeCatalogReadModel>>
        ListActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Query().ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    public async Task<FrameTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Query().SingleOrDefaultAsync(
                value => value.Code == code,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    private IQueryable<FrameTypeCatalogReadModel> Query() =>
        dbContext.FrameTypes.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Code)
            .Select(value => new FrameTypeCatalogReadModel(
                value.Id,
                value.Code,
                value.Name,
                value.IsActive));
}

public sealed class FinishTypeCatalogRepository(
    ApplicationDbContext dbContext) : IFinishTypeCatalogRepository
{
    public async Task<IReadOnlyList<FinishTypeCatalogReadModel>>
        ListActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Query().ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    public async Task<FinishTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Query().SingleOrDefaultAsync(
                value => value.Code == code,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    private IQueryable<FinishTypeCatalogReadModel> Query() =>
        dbContext.FinishTypes.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Code)
            .Select(value => new FinishTypeCatalogReadModel(
                value.Id,
                value.Code,
                value.Name,
                value.RequiresReview,
                value.IsActive));
}

public sealed class CatalogAliasRepository(
    ApplicationDbContext dbContext) : ICatalogAliasRepository
{
    public async Task<IReadOnlyList<CatalogAliasReadModel>> ListActiveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await Query().ToArrayAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    public async Task<CatalogAliasReadModel?> FindActiveByNormalizedAliasAsync(
        CatalogAliasCategory category,
        string normalizedAlias,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Query().SingleOrDefaultAsync(
                value => value.Category == category
                    && value.NormalizedAlias == normalizedAlias,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new CanonicalCatalogQueryException(exception);
        }
    }

    private IQueryable<CatalogAliasReadModel> Query() =>
        dbContext.CatalogAliases.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Category)
            .ThenBy(value => value.NormalizedAlias)
            .Select(value => new CatalogAliasReadModel(
                value.Id,
                value.Category,
                value.Alias,
                value.NormalizedAlias,
                value.CanonicalCode,
                value.MatchPolicy,
                value.RequiresContext,
                value.Confidence,
                value.IsActive));
}
