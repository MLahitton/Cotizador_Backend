using Infrastructure.HistoricalPricing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using System.Text.Json;

var path = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("HistoricalPricing__QuotesPath");
var options = new HistoricalPricingOptions(path, 20);
var corpus = new HistoricalQuoteCorpus(options, new HistoricalWorkbookReader());
await corpus.ReloadAsync();
var audit = corpus.Audit();
Console.WriteLine($"Configured path: {audit.ConfiguredPath ?? "<not configured>"}");
Console.WriteLine($"Total files: {audit.TotalFiles}");
Console.WriteLine($"OOXML: {audit.OoxmlFiles}");
Console.WriteLine($"OLE/CDFV2: {audit.OleFiles}");
Console.WriteLine($"Unknown: {audit.UnknownFiles}");
Console.WriteLine($"Duplicates: {audit.DuplicateFiles}");
Console.WriteLine($"Unique processable: {audit.UniqueProcessableFiles}");
Console.WriteLine($"Quotes parsed: {audit.QuotesParsed}");
Console.WriteLine($"Items parsed: {audit.ItemsParsed}");
Console.WriteLine($"Pricing-capable items: {audit.PricingCapableItems}");
Console.WriteLine($"Items with area: {audit.ItemsWithArea}");
Console.WriteLine($"Items with system: {audit.ItemsWithSystem}");
Console.WriteLine($"Items with glass: {audit.ItemsWithGlass}");
Console.WriteLine($"Items with finish: {audit.ItemsWithFinish}");
Console.WriteLine($"Area mismatches: {audit.AreaMismatches}");
var filters = args.Skip(1).Where(value => !value.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (filters.Length == 0) filters = ["SG 943"];
foreach (var quote in corpus.Current.Quotes.Where(quote => filters.Any(filter => quote.Source.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))))
{
    Console.WriteLine($"Quote source: {quote.Source.FileName}");
    Console.WriteLine($"Quote items: {quote.Items.Count}");
    Console.WriteLine($"Quote area: {quote.Items.Sum(item => item.ReportedArea ?? item.DerivedArea ?? 0m):0.####}");
    Console.WriteLine($"Quote unit-price sum: {quote.Items.Sum(item => item.PublicUnitPrice ?? 0m):0.##}");
    Console.WriteLine($"Quote item-total sum: {quote.Items.Sum(item => item.PublicTotal ?? 0m):0.##}");
    Console.WriteLine($"Quote document total: {quote.DocumentCommercialTotal:0.##}");
}

if (args.Contains("--candidate-validation", StringComparer.Ordinal))
{
    var service = new HistoricalComparableCandidateService(corpus, options);
    var cases = new[]
    {
        ("A", new HistoricalCandidateQuery("PUERTA", "3831", "TEMPLADO 6MM", 6m, "CORREDIZA", 3.740m, 2.500m, 9.35m, null, 1m, 10)),
        ("B", new HistoricalCandidateQuery("VENTANA", "8025", "TEMPLADO 6MM", 6m, "CORREDIZA", 3.090m, 1.900m, 5.87m, null, 1m, 10)),
        ("C", new HistoricalCandidateQuery("VENTANA", "3831", "TEMPLADO 6MM", 6m, null, 0.600m, 1.800m, 1.08m, null, 4m, 10))
    };
    foreach (var candidateCase in cases)
    {
        Console.WriteLine($"=== CANDIDATE CASE {candidateCase.Item1} ===");
        var rank = 0;
        foreach (var candidate in service.Find(candidateCase.Item2))
        {
            rank++;
            Console.WriteLine(string.Join("\t", rank, candidate.HistoricalQuoteId,
                candidate.HistoricalItemId, candidate.HistoricalReference,
                candidate.Description, candidate.System, $"{candidate.Glass}/{candidate.GlassThickness}",
                candidate.Area, candidate.PublicUnitPrice, candidate.PreliminaryScore,
                string.Join(',', candidate.MatchedSignals),
                string.Join(',', candidate.MissingSignals)));
        }
    }
}

if (args.Contains("--similarity-smoke", StringComparer.Ordinal))
{
    var endpoint = ReadSimilarityEndpoint();
    if (endpoint is null)
    {
        Console.WriteLine("Similarity smoke: AI2_SIMILARITY_NOT_CONFIGURED");
        return;
    }

    var candidateService = new HistoricalComparableCandidateService(corpus, options);
    using var httpClient = new HttpClient();
    var ai2Client = new Ai2SimilarityClient(
        httpClient,
        new Ai2SimilarityOptions(endpoint));
    var similarityService = new EvaluateHistoricalSimilarityService(
        candidateService,
        ai2Client);
    var query = new HistoricalCandidateQuery(
        "PUERTA",
        "3831",
        "TEMPLADO",
        6m,
        "CORREDIZA",
        null,
        null,
        9.35m,
        null,
        1m,
        5);

    Console.WriteLine($"Similarity endpoint: {endpoint}");
    var result = await similarityService.EvaluateAsync(query);
    Console.WriteLine($"Similarity status: {result.Status}");
    Console.WriteLine($"Similarity failure: {result.FailureCode ?? "<none>"}");
    Console.WriteLine($"Candidates: {result.Candidates.Count}");
    foreach (var value in result.Candidates)
    {
        var candidate = value.Candidate;
        var similarity = value.Similarity;
        Console.WriteLine(string.Join("\t",
            candidate.HistoricalItemId,
            candidate.HistoricalQuoteId,
            candidate.HistoricalReference,
            candidate.PreliminaryScore,
            similarity?.SimilarityScore.ToString() ?? "<unavailable>",
            similarity?.SimilarityLevel ?? "<unavailable>",
            similarity?.Confidence.ToString() ?? "<unavailable>",
            similarity is null ? "<unavailable>" : string.Join(',', similarity.MatchedFeatures),
            similarity is null ? "<unavailable>" : string.Join(" | ", similarity.Differences),
            similarity?.TechnicalExplanation ?? "<unavailable>"));
    }

    Console.WriteLine("=== BACKEND-ONLY PRICE AUDIT (NOT SENT TO AI2) ===");
    foreach (var value in result.Candidates)
    {
        Console.WriteLine($"{value.Candidate.HistoricalItemId}\t{value.Candidate.PublicUnitPrice}");
    }
}

if (args.Contains("--price-smoke", StringComparer.Ordinal))
{
    var endpoint = ReadSimilarityEndpoint();
    if (endpoint is null)
    {
        Console.WriteLine("Price smoke: AI2_SIMILARITY_NOT_CONFIGURED");
        return;
    }
    var candidateService = new HistoricalComparableCandidateService(corpus, options);
    using var httpClient = new HttpClient();
    var similarityService = new EvaluateHistoricalSimilarityService(
        candidateService,
        new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(endpoint)));
    var estimator = new HistoricalTechnicalPriceEstimator(similarityService);
    var query = new HistoricalCandidateQuery(
        "PUERTA", "3831", "TEMPLADO", 6m, "CORREDIZA",
        null, null, 9.35m, null, 1m, 5);
    var estimate = await estimator.EstimateAsync(query);
    Console.WriteLine("=== TECHNICAL PRICE CASE A ===");
    foreach (var value in estimate.Comparables)
    {
        Console.WriteLine(string.Join("\t", value.HistoricalReference,
            value.PublicUnitPrice, value.BackendTechnicalScore,
            value.Ai2SimilarityScore, value.FinalWeight,
            value.HistoricalUnitArea, value.ProjectedPrice));
    }
    Console.WriteLine($"Minimum: {estimate.Minimum}");
    Console.WriteLine($"Expected: {estimate.Expected}");
    Console.WriteLine($"Maximum: {estimate.Maximum}");
    Console.WriteLine($"Confidence: {estimate.ConfidenceScore} {estimate.ConfidenceLevel}");
    Console.WriteLine($"Strong comparables: {estimate.StrongComparableCount}");
    Console.WriteLine($"Requires review: {estimate.RequiresReview}");
}

if (args.Contains("--price-eval-sg943-item03", StringComparer.Ordinal))
{
    const string quoteId = "DA46FFCDC3A243008F23A0BA5BFC7AB1211DB09000983896F78EB68F7BDEE99A";
    const string candidateId = quoteId + ":3";
    const decimal realPrice = 8380363.338m;
    var endpoint = ReadSimilarityEndpoint();
    if (endpoint is null)
    {
        Console.WriteLine("Price evaluation: AI2_SIMILARITY_NOT_CONFIGURED");
        return;
    }

    var candidateService = new HistoricalComparableCandidateService(corpus, options);
    using var httpClient = new HttpClient();
    var estimator = new HistoricalTechnicalPriceEstimator(
        new EvaluateHistoricalSimilarityService(
            candidateService,
            new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(endpoint))));
    var baseQuery = new HistoricalCandidateQuery(
        "PUERTA", "VENECIA NAPOLES", "TEMPLADO", 8m, "CORREDIZA",
        4560m, 2650m, 12.084m, null, 1m, 10);

    await Evaluate("EXCLUDE_CANDIDATE", baseQuery with
    {
        ExcludedCandidateIds = [candidateId]
    });
    await Evaluate("EXCLUDE_QUOTE", baseQuery with
    {
        ExcludedQuoteIds = [quoteId]
    });

    async Task Evaluate(string name, HistoricalCandidateQuery query)
    {
        var estimate = await estimator.EstimateAsync(query);
        decimal? absoluteError = estimate.Expected is null
            ? null
            : Math.Abs(estimate.Expected.Value - realPrice);
        var percentageError = absoluteError / realPrice * 100m;
        var inside = estimate.Minimum is not null && estimate.Maximum is not null
            && realPrice >= estimate.Minimum && realPrice <= estimate.Maximum;
        Console.WriteLine($"=== {name} ===");
        Console.WriteLine($"Minimum: {estimate.Minimum}");
        Console.WriteLine($"Expected: {estimate.Expected}");
        Console.WriteLine($"Maximum: {estimate.Maximum}");
        Console.WriteLine($"Confidence: {estimate.ConfidenceScore} {estimate.ConfidenceLevel}");
        Console.WriteLine($"Real price: {realPrice}");
        Console.WriteLine($"Absolute error: {absoluteError}");
        Console.WriteLine($"Percentage error: {percentageError}");
        Console.WriteLine($"Real price inside range: {inside}");
    }
}

static Uri? ReadSimilarityEndpoint()
{
    var environmentValue = Environment.GetEnvironmentVariable(
        "CotizadorAi2__SimilarityEndpoint");
    if (Uri.TryCreate(environmentValue, UriKind.Absolute, out var environmentEndpoint))
    {
        return environmentEndpoint;
    }

    var configurationPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "Api",
        "appsettings.Development.json");
    if (!File.Exists(configurationPath))
    {
        return null;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
    if (!document.RootElement.TryGetProperty("CotizadorAi2", out var ai2)
        || !ai2.TryGetProperty("SimilarityEndpoint", out var endpointProperty))
    {
        return null;
    }

    return Uri.TryCreate(endpointProperty.GetString(), UriKind.Absolute, out var endpoint)
        ? endpoint
        : null;
}
