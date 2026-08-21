using Domain.Catalogs;

namespace Application.Common.Abstractions.Catalogs;

public sealed record GlassPriceRangeCatalogReadModel(
    Guid GlassPriceRangeVersionId,
    int Version,
    decimal MinimumPricePerSquareMeter,
    decimal ExpectedAmountPerM2,
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
    GlassPriceRangeCatalogReadModel? CurrentPriceRange,
    string? Family = null,
    string? Composition = null,
    string? Treatment = null,
    decimal? OuterThicknessMm = null,
    decimal? InnerThicknessMm = null,
    decimal? PvbThicknessMm = null,
    string? PvbType = null,
    string? PvbColor = null,
    decimal? ChamberThicknessMm = null,
    string? ProductLine = null,
    string? ProductToken = null,
    string? Pattern = null,
    string? Color = null,
    bool IsSelectable = true,
    bool RequiresReview = false);

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
