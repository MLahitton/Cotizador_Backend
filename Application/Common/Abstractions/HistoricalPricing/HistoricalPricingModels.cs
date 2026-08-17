namespace Application.Common.Abstractions.HistoricalPricing;

public enum HistoricalWorkbookContainerType { Empty, Ooxml, OleCdfV2, Unknown }

public sealed record HistoricalQuoteIssue(string Code, string Message, string? SourceCell = null);

public sealed record HistoricalWorkbookInspection(
    string FileName, string SourceIdentifier, string Sha256,
    HistoricalWorkbookContainerType ContainerType, bool IsProcessable,
    IReadOnlyList<string> SheetNames, bool HasQuotationSheet,
    IReadOnlyList<HistoricalQuoteIssue> Issues,
    IReadOnlyList<string> DuplicateFileNames);

public sealed record HistoricalQuoteSource(
    string FileName, string SourceIdentifier, string Sha256, string SheetName,
    IReadOnlyList<string> DuplicateFileNames);

public sealed record HistoricalQuoteItem(
    string Id, string? Reference, string? Description, string? Location,
    string? ConfigurationRaw, string? ConfigurationNormalized,
    decimal? Width, decimal? Height, string? WidthRaw, string? HeightRaw,
    decimal? ReportedArea, decimal? DerivedArea, decimal? Quantity,
    string? QuantityRaw, string? Category,
    string? SystemRaw, string? SystemNormalized,
    string? GlassRaw, string? GlassFamily, decimal? GlassThickness,
    string? GlassComposition, string? FinishRaw, string? FinishNormalized,
    decimal? PublicUnitPrice, decimal? PublicTotal, string? Notes,
    string SourceSheet, IReadOnlyList<string> SourceCells,
    IReadOnlyList<HistoricalQuoteIssue> Issues)
{
    public bool IsPricingCapable => PublicUnitPrice is > 0;
}

public sealed record HistoricalQuote(
    string Id, string? QuoteId, string? Client, string? Project,
    string? Location, string? Version, string Currency,
    decimal? DocumentCommercialTotal,
    HistoricalQuoteSource Source, IReadOnlyList<HistoricalQuoteItem> Items,
    IReadOnlyList<HistoricalQuoteIssue> Issues);

public sealed record HistoricalCorpusSnapshot(
    bool IsAvailable, string? ConfiguredPath, DateTimeOffset? LoadedAtUtc,
    IReadOnlyList<HistoricalWorkbookInspection> Inspections,
    IReadOnlyList<HistoricalQuote> Quotes)
{
    public static HistoricalCorpusSnapshot Unavailable(string? path) =>
        new(false, path, null, [], []);
}

public sealed record HistoricalCorpusAudit(
    string? ConfiguredPath, int TotalFiles, int OoxmlFiles, int OleFiles,
    int UnknownFiles, int DuplicateFiles, int UniqueProcessableFiles,
    int QuotesParsed, int ItemsParsed, int PricingCapableItems,
    int ItemsWithArea, int ItemsWithSystem, int ItemsWithGlass,
    int ItemsWithFinish, int AreaMismatches);

public sealed record HistoricalCandidateQuery(
    string? Category, string? System, string? Glass,
    decimal? GlassThickness, string? Configuration,
    decimal? Width, decimal? Height, decimal? Area,
    string? Finish, decimal? Quantity, int? Top = null,
    IReadOnlyCollection<string>? ExcludedCandidateIds = null,
    IReadOnlyCollection<string>? ExcludedQuoteIds = null,
    string? GlassComposition = null);

public sealed record HistoricalComparableCandidate(
    string HistoricalQuoteId, string HistoricalItemId,
    string? HistoricalReference, string? Description,
    decimal PublicUnitPrice, decimal? PublicTotal,
    string? Category, string? System, string? Glass, decimal? GlassThickness,
    string? GlassComposition, string? Configuration,
    decimal? Width, decimal? Height, decimal? Area, decimal? Quantity,
    string? Finish,
    decimal PreliminaryScore, IReadOnlyList<string> MatchedSignals,
    IReadOnlyList<string> MissingSignals,
    bool HasAreaMismatch);

public interface IHistoricalQuoteCorpus
{
    HistoricalCorpusSnapshot Current { get; }
    Task<HistoricalCorpusSnapshot> ReloadAsync(CancellationToken cancellationToken = default);
    HistoricalCorpusAudit Audit();
}

public interface IHistoricalComparableCandidateService
{
    IReadOnlyList<HistoricalComparableCandidate> Find(HistoricalCandidateQuery query);
}

public static class HistoricalCandidateRankingWeights
{
    public const decimal Category = 35m;
    public const decimal System = 20m;
    public const decimal GlassFamily = 15m;
    public const decimal GlassThickness = 15m;
    public const decimal Configuration = 10m;
    public const decimal Area = 12m;
    public const decimal ApproximateArea = 6m;
    public const decimal Finish = 5m;
    public const decimal Quantity = 3m;
    public const decimal MaximumScore = Category + System + GlassFamily
        + GlassThickness + Configuration + Area + Finish + Quantity;
    public const decimal ThicknessTolerance = 0.1m;
    public const decimal ExactAreaTolerance = 0.10m;
    public const decimal ApproximateAreaTolerance = 0.25m;
}
