namespace Contracts.Catalogs;

public sealed record GlassPriceRangeResponse(
    Guid GlassPriceRangeVersionId,
    int Version,
    decimal MinimumPricePerSquareMeter,
    decimal ExpectedAmountPerM2,
    decimal MaximumPricePerSquareMeter,
    string Currency,
    string Status,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc);

public sealed record GlassTypeCatalogItemResponse(
    Guid GlassTypeId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    GlassPriceRangeResponse? CurrentPriceRange);

public sealed record GetGlassTypesCatalogResponse(
    IReadOnlyList<GlassTypeCatalogItemResponse> Items);
