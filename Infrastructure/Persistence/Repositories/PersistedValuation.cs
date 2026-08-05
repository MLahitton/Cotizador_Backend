using Domain.Catalogs;
using Domain.PreQuotes;

namespace Infrastructure.Persistence.Repositories;

internal sealed record PersistedValuation(
    int ItemSequence,
    GlassValuationStatus Status,
    GlassValuationReason? Reason,
    Guid? GlassTypeId,
    Guid? GlassPriceRangeVersionId,
    int? PriceRangeVersion,
    GlassPriceRangeStatus? PriceRangeStatus,
    string? Currency,
    decimal? UnitAreaSquareMeters,
    decimal? TotalAreaSquareMeters,
    decimal? MinimumPricePerSquareMeter,
    decimal? ExpectedPricePerSquareMeter,
    decimal? MaximumPricePerSquareMeter,
    decimal? MinimumAmount,
    decimal? ExpectedAmount,
    decimal? MaximumAmount,
    DateTimeOffset CalculatedAtUtc);
