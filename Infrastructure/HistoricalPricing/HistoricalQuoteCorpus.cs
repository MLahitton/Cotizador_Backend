using Application.Common.Abstractions.HistoricalPricing;

namespace Infrastructure.HistoricalPricing;

public sealed class HistoricalQuoteCorpus : IHistoricalQuoteCorpus
{
    private readonly HistoricalPricingOptions _options;
    private readonly HistoricalWorkbookReader _reader;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private HistoricalCorpusSnapshot _current;

    public HistoricalQuoteCorpus(HistoricalPricingOptions options, HistoricalWorkbookReader reader)
    {
        _options = options;
        _reader = reader;
        _current = HistoricalCorpusSnapshot.Unavailable(options.QuotesPath);
    }

    public HistoricalCorpusSnapshot Current => Volatile.Read(ref _current);

    public async Task<HistoricalCorpusSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(_options.QuotesPath) || !Directory.Exists(_options.QuotesPath))
            {
                var unavailable = HistoricalCorpusSnapshot.Unavailable(_options.QuotesPath);
                Volatile.Write(ref _current, unavailable);
                return unavailable;
            }

            var root = Path.GetFullPath(_options.QuotesPath);
            var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var raw = files.Select(path => _reader.Inspect(path, Path.GetRelativePath(root, path))).ToArray();
            var namesByHash = raw.Where(item => item.Sha256.Length > 0)
                .GroupBy(item => item.Sha256, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.FileName).ToArray());
            var inspections = raw.Select(item => item with
            {
                DuplicateFileNames = namesByHash.TryGetValue(item.Sha256, out var names) && names.Count > 1 ? names : []
            }).ToArray();
            var quotes = new List<HistoricalQuote>();
            foreach (var inspection in inspections.Where(item => item.IsProcessable)
                .GroupBy(item => item.Sha256, StringComparer.Ordinal).Select(group => group.First()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { quotes.Add(_reader.Parse(Path.Combine(root, inspection.SourceIdentifier), inspection)); }
                catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException) { }
            }

            var snapshot = new HistoricalCorpusSnapshot(true, root, DateTimeOffset.UtcNow, inspections, quotes);
            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }
        finally { _reloadLock.Release(); }
    }

    public HistoricalCorpusAudit Audit()
    {
        var snapshot = Current;
        var items = snapshot.Quotes.SelectMany(quote => quote.Items).ToArray();
        return new HistoricalCorpusAudit(
            snapshot.ConfiguredPath, snapshot.Inspections.Count,
            snapshot.Inspections.Count(item => item.ContainerType == HistoricalWorkbookContainerType.Ooxml),
            snapshot.Inspections.Count(item => item.ContainerType == HistoricalWorkbookContainerType.OleCdfV2),
            snapshot.Inspections.Count(item => item.ContainerType is HistoricalWorkbookContainerType.Unknown or HistoricalWorkbookContainerType.Empty),
            snapshot.Inspections.Where(item => item.Sha256.Length > 0).GroupBy(item => item.Sha256).Sum(group => Math.Max(0, group.Count() - 1)),
            snapshot.Inspections.Where(item => item.IsProcessable).Select(item => item.Sha256).Distinct(StringComparer.Ordinal).Count(),
            snapshot.Quotes.Count, items.Length, items.Count(item => item.IsPricingCapable),
            items.Count(item => item.ReportedArea is not null || item.DerivedArea is not null),
            items.Count(item => item.SystemRaw is not null), items.Count(item => item.GlassRaw is not null),
            items.Count(item => item.FinishRaw is not null),
            items.Sum(item => item.Issues.Count(issue => issue.Code == "HistoricalAreaMismatch")));
    }
}

public sealed class HistoricalComparableCandidateService : IHistoricalComparableCandidateService
{
    private readonly IHistoricalQuoteCorpus _corpus;
    private readonly HistoricalPricingOptions _options;

    public HistoricalComparableCandidateService(IHistoricalQuoteCorpus corpus, HistoricalPricingOptions options)
    {
        _corpus = corpus;
        _options = options;
    }

    public IReadOnlyList<HistoricalComparableCandidate> Find(HistoricalCandidateQuery query)
    {
        var category = HistoricalQuoteNormalizer.NormalizeText(query.Category);
        var system = HistoricalQuoteNormalizer.NormalizeText(query.System);
        var glass = HistoricalQuoteNormalizer.GlassFamily(query.Glass) ?? HistoricalQuoteNormalizer.NormalizeText(query.Glass);
        var configuration = HistoricalQuoteNormalizer.NormalizeText(query.Configuration);
        var finish = HistoricalQuoteNormalizer.NormalizeText(query.Finish);
        var excludedCandidateIds = new HashSet<string>(
            query.ExcludedCandidateIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        var excludedQuoteIds = new HashSet<string>(
            query.ExcludedQuoteIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        var top = Math.Clamp(query.Top ?? _options.CandidateTopK, 1, 100);
        return _corpus.Current.Quotes.SelectMany(quote => quote.Items.Select(item => (quote, item)))
            .Where(pair => pair.item.IsPricingCapable)
            .Where(pair => category is null || pair.item.Category is null || pair.item.Category == category)
            .Where(pair => !excludedQuoteIds.Contains(pair.quote.Id))
            .Where(pair => !excludedCandidateIds.Contains(pair.item.Id))
            .DistinctBy(pair => (pair.quote.Id, pair.item.Id))
            .Select(pair => Score(pair.quote, pair.item))
            .Where(candidate => candidate.PreliminaryScore > 0)
            .OrderByDescending(candidate => candidate.PreliminaryScore)
            .ThenBy(candidate => candidate.HistoricalQuoteId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.HistoricalItemId, StringComparer.Ordinal)
            .Take(top).ToArray();

        HistoricalComparableCandidate Score(HistoricalQuote quote, HistoricalQuoteItem item)
        {
            decimal score = 0;
            var matched = new List<string>();
            var missing = new List<string>();
            AddExact("category", category, item.Category, HistoricalCandidateRankingWeights.Category);
            AddText("system", system, item.SystemNormalized, item.Description, HistoricalCandidateRankingWeights.System);
            AddExact("glass", glass, item.GlassFamily, HistoricalCandidateRankingWeights.GlassFamily);
            AddText("configuration", configuration, item.ConfigurationNormalized, item.Description, HistoricalCandidateRankingWeights.Configuration);
            AddExact("finish", finish, item.FinishNormalized, HistoricalCandidateRankingWeights.Finish);
            if (query.GlassThickness is not null)
            {
                if (item.GlassThickness is null) missing.Add("glassThickness");
                else if (Math.Abs(item.GlassThickness.Value - query.GlassThickness.Value) <= HistoricalCandidateRankingWeights.ThicknessTolerance)
                { score += HistoricalCandidateRankingWeights.GlassThickness; matched.Add("glassThickness"); }
                else missing.Add("glassThickness");
            }
            var quantity = query.Quantity is > 0 ? query.Quantity.Value : 1m;
            var expectedArea = query.Area is not null
                ? query.Area.Value * quantity
                : query.Width is not null && query.Height is not null
                    ? query.Width * query.Height * quantity
                    : null;
            var actualArea = item.ReportedArea ?? item.DerivedArea;
            if (expectedArea is not null)
            {
                if (actualArea is null) missing.Add("area");
                else
                {
                    var ratio = Math.Abs(actualArea.Value - expectedArea.Value) / Math.Max(expectedArea.Value, 0.01m);
                    if (ratio <= HistoricalCandidateRankingWeights.ExactAreaTolerance)
                    { score += HistoricalCandidateRankingWeights.Area; matched.Add("area"); }
                    else if (ratio <= HistoricalCandidateRankingWeights.ApproximateAreaTolerance)
                    { score += HistoricalCandidateRankingWeights.ApproximateArea; matched.Add("areaApproximate"); }
                    else missing.Add("area");
                }
            }
            if (query.Quantity is not null)
            {
                if (item.Quantity is null) missing.Add("quantity");
                else if (item.Quantity == query.Quantity)
                { score += HistoricalCandidateRankingWeights.Quantity; matched.Add("quantity"); }
                else missing.Add("quantity");
            }
            return new HistoricalComparableCandidate(quote.Id, item.Id, item.Reference, item.Description, item.PublicUnitPrice!.Value,
                item.PublicTotal, item.Category, item.SystemNormalized, item.GlassFamily, item.GlassThickness,
                item.GlassComposition, item.ConfigurationNormalized, item.Width, item.Height, actualArea,
                item.Quantity, item.FinishNormalized, score, matched, missing,
                item.Issues.Any(issue => issue.Code == "HistoricalAreaMismatch"));

            void AddExact(string signal, string? expected, string? actual, decimal weight)
            {
                if (expected is null) return;
                if (actual is null) missing.Add(signal);
                else if (actual == expected) { score += weight; matched.Add(signal); }
                else missing.Add(signal);
            }
            void AddText(string signal, string? expected, string? primary, string? fallback, decimal weight)
            {
                if (expected is null) return;
                if (TextMatches(expected, primary) || TextMatches(expected, HistoricalQuoteNormalizer.NormalizeText(fallback)))
                { score += weight; matched.Add(signal); }
                else missing.Add(signal);
            }
        }
    }

    private static bool TextMatches(string expected, string? actual)
    {
        if (actual is null) return false;
        if (actual == expected) return true;
        return (" " + actual + " ").Contains(" " + expected + " ", StringComparison.Ordinal);
    }
}
