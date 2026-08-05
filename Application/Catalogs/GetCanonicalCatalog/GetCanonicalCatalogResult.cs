using Application.Common.Abstractions.Catalogs;

namespace Application.Catalogs.GetCanonicalCatalog;

public enum GetCanonicalCatalogFailure
{
    None = 0,
    Unauthorized,
    InactiveUser,
    QueryError
}

public sealed record GetCanonicalCatalogResult(
    GetCanonicalCatalogFailure Failure,
    IReadOnlyList<ProductSystemCatalogReadModel> Systems,
    IReadOnlyList<FrameTypeCatalogReadModel> Frames,
    IReadOnlyList<FinishTypeCatalogReadModel> Finishes,
    IReadOnlyList<CatalogAliasReadModel> Aliases)
{
    public bool IsSuccess => Failure == GetCanonicalCatalogFailure.None;

    public static GetCanonicalCatalogResult Success(
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<FrameTypeCatalogReadModel> frames,
        IReadOnlyList<FinishTypeCatalogReadModel> finishes,
        IReadOnlyList<CatalogAliasReadModel> aliases) =>
        new(GetCanonicalCatalogFailure.None, systems, frames, finishes, aliases);

    public static GetCanonicalCatalogResult Failed(
        GetCanonicalCatalogFailure failure) => new(failure, [], [], [], []);
}
