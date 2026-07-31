using Domain.Catalogs;

namespace Application.Common.Abstractions.Catalogs;

public sealed record GlassPriceRangeCatalogReadModel(
    Guid GlassPriceRangeVersionId,
    int Version,
    decimal MinimumPricePerSquareMeter,
    decimal MaximumPricePerSquareMeter,
    string Currency,
    GlassPriceRangeStatus Status,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc);

public sealed record GlassTypeCatalogReadModel(
    Guid GlassTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    GlassPriceRangeCatalogReadModel? CurrentPriceRange);

public interface IGlassTypeCatalogRepository
{
    Task<IReadOnlyList<GlassTypeCatalogReadModel>>
        GetActiveWithCurrentPriceRangesAsync(
            CancellationToken cancellationToken);

    Task<GlassTypeCatalogReadModel?>
        GetActiveByCodeWithCurrentPriceRangeAsync(
            string normalizedCode,
            CancellationToken cancellationToken);
}

public sealed class GlassTypeCatalogQueryException : Exception
{
    public GlassTypeCatalogQueryException(Exception innerException)
        : base("No fue posible consultar el catalogo de vidrios.", innerException)
    {
    }
}
