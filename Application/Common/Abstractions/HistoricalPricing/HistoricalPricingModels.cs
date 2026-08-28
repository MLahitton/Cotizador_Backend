using System.Globalization;
using System.Text;

namespace Application.Common.Abstractions.HistoricalPricing;

public static class HistoricalSystemIdentity
{
    public static string? Canonicalize(string? value)
    {
        var text = Normalize(value);
        if (text is null) return null;
        var variant = ContainsAny(text, "POCKET", "POKET") ? "POCKET"
            : text.Contains("INOX", StringComparison.Ordinal) ? "INOX"
            : ContainsAny(text, "DOUBLE", "DOBLE") ? "DOUBLE"
            : "STANDARD";
        var family = ContainsAny(text, "VENECIA FERMO", "SERIE 40") ? "FERMO"
            : ContainsAny(text, "VENECIA MONZA", "SERIE 50") ? "MONZA"
            : ContainsAny(text, "VENECIA NAPOLES", "SERIE 70") ? "NAPOLES"
            : ContainsAny(text, "VENECIA MONACO", "SERIE 100") ? "MONACO"
            : ContainsAny(text, "PRIMAVERA SIENA", "SG 4", "SG4") ? "SIENA"
            : ContainsAny(text, "PRIMAVERA LAGO", "SG 5", "SG5") ? "LAGO"
            : ContainsAny(text, "PRIMAVERA LUCCA", "SG 8", "SG8") ? "LUCCA"
            : null;
        return family is null ? null : $"{family}:{variant}";
    }

    public static bool Matches(string? left, string? right)
    {
        var leftIdentity = Canonicalize(left);
        var rightIdentity = Canonicalize(right);
        return leftIdentity is not null && rightIdentity is not null
            ? leftIdentity == rightIdentity
            : !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(
                    Normalize(left),
                    Normalize(right),
                    StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder();
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return string.Join(' ', builder.ToString().Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.Ordinal));
}

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
    string? GlassComposition = null,
    string? CommercialLine = null,
    bool RequireSystemMatchedComparable = false);

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
    bool HasAreaMismatch,
    string MatchingTier = "UNSPECIFIED",
    IReadOnlyList<string>? FallbackReasons = null,
    bool MatchedSystem = false,
    bool MatchedGlass = false,
    bool MatchedFinish = false,
    bool MatchedCommercialLine = false);

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
