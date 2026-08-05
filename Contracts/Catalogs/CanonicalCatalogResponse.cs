namespace Contracts.Catalogs;

public sealed record CanonicalCatalogSystemResponse(
    string Code,
    string Name,
    bool ActiveForRecognition,
    bool Priceable,
    bool FuturePriceable,
    bool RequiresReview,
    bool IsActive);

public sealed record CanonicalCatalogFrameResponse(
    string Code,
    string Name,
    bool IsActive);

public sealed record CanonicalCatalogFinishResponse(
    string Code,
    string Name,
    bool RequiresReview,
    bool IsActive);

public sealed record CanonicalCatalogAliasResponse(
    string Category,
    string Alias,
    string NormalizedAlias,
    string CanonicalCode,
    string MatchPolicy,
    bool RequiresContext,
    decimal Confidence,
    bool IsActive);

public sealed record GetCanonicalCatalogResponse(
    IReadOnlyList<CanonicalCatalogSystemResponse> Systems,
    IReadOnlyList<CanonicalCatalogFrameResponse> Frames,
    IReadOnlyList<CanonicalCatalogFinishResponse> Finishes,
    IReadOnlyList<CanonicalCatalogAliasResponse> Aliases);
