using Domain.Catalogs;

namespace Application.Common.Abstractions.Catalogs;

public sealed record ProductSystemCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    bool ActiveForRecognition,
    bool Priceable,
    bool FuturePriceable,
    bool RequiresReview,
    bool IsActive);

public sealed record FrameTypeCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed record FinishTypeCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    bool RequiresReview,
    bool IsActive);

public sealed record CatalogAliasReadModel(
    Guid Id,
    CatalogAliasCategory Category,
    string Alias,
    string NormalizedAlias,
    string CanonicalCode,
    CatalogAliasMatchPolicy MatchPolicy,
    bool RequiresContext,
    decimal Confidence,
    bool IsActive);

public interface IProductSystemCatalogRepository
{
    Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface IFrameTypeCatalogRepository
{
    Task<IReadOnlyList<FrameTypeCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<FrameTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface IFinishTypeCatalogRepository
{
    Task<IReadOnlyList<FinishTypeCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<FinishTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface ICatalogAliasRepository
{
    Task<IReadOnlyList<CatalogAliasReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<CatalogAliasReadModel?> FindActiveByNormalizedAliasAsync(
        CatalogAliasCategory category,
        string normalizedAlias,
        CancellationToken cancellationToken);
}

public sealed class CanonicalCatalogQueryException : Exception
{
    public CanonicalCatalogQueryException(Exception innerException)
        : base("No fue posible consultar el catalogo canonico.", innerException)
    {
    }
}
