using Application.Common.Abstractions.Catalogs;

namespace Application.Catalogs.GetGlassTypesCatalog;

public enum GetGlassTypesCatalogFailure
{
    None = 0,
    Unauthorized,
    InactiveUser,
    QueryError
}

public sealed record GetGlassTypesCatalogResult(
    GetGlassTypesCatalogFailure Failure,
    IReadOnlyList<GlassTypeCatalogReadModel> Items)
{
    public bool IsSuccess => Failure == GetGlassTypesCatalogFailure.None;

    public static GetGlassTypesCatalogResult Success(
        IReadOnlyList<GlassTypeCatalogReadModel> items) =>
        new(GetGlassTypesCatalogFailure.None, items);

    public static GetGlassTypesCatalogResult Failed(
        GetGlassTypesCatalogFailure failure) =>
        new(failure, []);
}
