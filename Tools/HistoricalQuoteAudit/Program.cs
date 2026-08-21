using Infrastructure.HistoricalPricing;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
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
        corpus,
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
        corpus,
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
            corpus,
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

if (args.Contains("--pricing-calibration-audit", StringComparer.Ordinal))
{
    var endpoint = ReadSimilarityEndpoint();
    var candidateService = new HistoricalComparableCandidateService(corpus, options);
    using var httpClient = new HttpClient();
    var similarityService = new EvaluateHistoricalSimilarityService(
        corpus,
        candidateService,
        endpoint is null
            ? new FailingSimilarityClient("AI2_SIMILARITY_NOT_CONFIGURED")
            : new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(endpoint)));
    var estimator = new HistoricalTechnicalPriceEstimator(similarityService);

    var cases = new[]
    {
        new CalibrationCase("PV-09", "control positivo",
            new HistoricalCandidateQuery("PUERTA",
                "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
                "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
                "CORREDIZA", null, null, 5.45m,
                "ALUCOLOR POLIESTER NEGRO MATE PP13", 2m, 20),
            8_102_000m),
        new CalibrationCase("V-23", "pricing puro defectuoso",
            new HistoricalCandidateQuery("VENTANA",
                "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
                "FIJO", null, null, 2.28m,
                "ALUCOLOR POLIESTER NEGRO MATE PP13", 1m, 20),
            1_075_000m),
        new CalibrationCase("PV-15", "area grande corregida",
            new HistoricalCandidateQuery("PUERTA",
                "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
                "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
                "CORREDIZA", 5320m, 2500m, 13.30m,
                "ALUCOLOR POLIESTER NEGRO MATE PP13", 1m, 20),
            8_421_000m),
        new CalibrationCase("V-08", "error mixto TEMP_6",
            new HistoricalCandidateQuery("VENTANA",
                "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
                "FIJO", 3800m, 1900m, 7.22m,
                "ALUCOLOR POLIESTER NEGRO MATE PP13", 4m, 20),
            11_440_000m)
    };

    Console.WriteLine("=== PRICING CALIBRATION AUDIT ===");
    Console.WriteLine($"Similarity endpoint: {endpoint?.ToString() ?? "<not configured>"}");
    Console.WriteLine($"Holdout corpus quotes: {corpus.Current.Quotes.Count}");
    Console.WriteLine($"Holdout corpus items: {corpus.Current.Quotes.Sum(quote => quote.Items.Count)}");
    foreach (var calibrationCase in cases)
    {
        await PrintCalibrationCase(calibrationCase, candidateService, similarityService, estimator);
    }

    Console.WriteLine("=== V-08 TEMP_8 DIAGNOSTIC ===");
    await PrintCalibrationCase(cases[^1] with
    {
        Name = "V-08 TEMP_8",
        Notes = "diagnostico hipotetico sin cambiar Suggested",
        Query = cases[^1].Query with
        {
            Glass = "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            GlassThickness = 8m
        }
    }, candidateService, similarityService, estimator);

    PrintEconomyOfScale(corpus.Current);
    PrintValidationDesign();
}

if (args.Contains("--pricing-repro-audit", StringComparer.Ordinal))
{
    await RunPricingReproAudit(args, corpus, options);
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

static async Task RunPricingReproAudit(
    string[] args,
    HistoricalQuoteCorpus corpus,
    HistoricalPricingOptions options)
{
    var requirementId = ReadGuidArg(args, "--requirement")
        ?? Guid.Parse("384699ad-9c86-45ff-be01-ca506b492e31");
    var connectionString = Environment.GetEnvironmentVariable(
        "ConnectionStrings__DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("PRICING_REPRO_STOP: ConnectionStrings__DefaultConnection is not configured.");
        return;
    }

    var endpoint = ReadSimilarityEndpoint();
    if (endpoint is null)
    {
        Console.WriteLine("PRICING_REPRO_STOP: CotizadorAi2:SimilarityEndpoint is not configured.");
        return;
    }

    var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connectionString);
    await using var dbContext = new ApplicationDbContext(dbOptions.Options);
    var proposal = await new RequirementRepository(dbContext)
        .GetCurrentTechnicalProposalAsync(requirementId, CancellationToken.None);
    if (proposal is null)
    {
        Console.WriteLine($"PRICING_REPRO_STOP: technical proposal not found for requirement {requirementId}.");
        return;
    }

    var systems = (await new ProductSystemCatalogRepository(dbContext)
            .ListActiveAsync(CancellationToken.None))
        .ToDictionary(value => value.Id);
    var glasses = (await new GlassTypeCatalogRepository(dbContext)
            .GetActiveWithCurrentPriceRangesAsync(CancellationToken.None))
        .ToDictionary(value => value.GlassTypeId);
    var finishes = (await new FinishTypeCatalogRepository(dbContext)
            .ListActiveAsync(CancellationToken.None))
        .ToDictionary(value => value.Id);

    var mapper = new TechnicalProposalItemToHistoricalPricingMapper();
    var candidateService = new HistoricalComparableCandidateService(corpus, options);
    using var httpClient = new HttpClient();
    var similarityService = new EvaluateHistoricalSimilarityService(
        corpus,
        candidateService,
        new Ai2SimilarityClient(httpClient, new Ai2SimilarityOptions(endpoint)));
    var toolQueries = CalibrationCases().ToDictionary(value => value.Name);

    Console.WriteLine("=== PRICING REPRO AUDIT ===");
    Console.WriteLine($"RequirementId={requirementId}");
    Console.WriteLine($"SimilarityEndpoint={endpoint}");
    Console.WriteLine($"QuotesParsed={corpus.Current.Quotes.Count}");
    Console.WriteLine($"ItemsParsed={corpus.Current.Quotes.Sum(quote => quote.Items.Count)}");

    foreach (var reference in new[] { "PV-09", "V-23", "PV-15", "V-08" })
    {
        var proposalItem = proposal.Items
            .OrderBy(value => value.ExtractedItem.Sequence)
            .FirstOrDefault(value => string.Equals(
                value.ExtractedItem.Reference,
                reference,
                StringComparison.OrdinalIgnoreCase));
        if (proposalItem is null)
        {
            Console.WriteLine($"=== {reference} ===");
            Console.WriteLine("RUNTIME_ITEM_NOT_FOUND");
            continue;
        }

        if (proposalItem.SuggestedSystemId is not { } systemId
            || proposalItem.SuggestedGlassTypeId is not { } glassId
            || proposalItem.SuggestedFinishTypeId is not { } finishId
            || !systems.TryGetValue(systemId, out var system)
            || !glasses.TryGetValue(glassId, out var glass)
            || !finishes.TryGetValue(finishId, out var finish))
        {
            Console.WriteLine($"=== {reference} ===");
            Console.WriteLine("RUNTIME_ITEM_NOT_PRICEABLE_BY_SUGGESTED_CATALOG");
            continue;
        }

        var runtime = mapper.Map(proposalItem, system, glass, finish).CandidateQuery;
        var tool = toolQueries[reference].Query;
        Console.WriteLine($"=== {reference} QUERY COMPARISON ===");
        PrintQueryComparison(runtime, tool);
        Console.WriteLine($"RealLinePrice={toolQueries[reference].RealLinePrice}; Source=PRICING.2A benchmark constants");
        await PrintStability(reference, runtime, candidateService, similarityService);
    }

    await PrintDeterministicRequirementBaseline(
        proposal,
        systems,
        glasses,
        finishes,
        mapper,
        corpus,
        candidateService,
        toolQueries);
}

static IReadOnlyList<CalibrationCase> CalibrationCases() =>
[
    new CalibrationCase("PV-09", "control positivo",
        new HistoricalCandidateQuery("PUERTA",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
            "CORREDIZA", null, null, 5.45m,
            "ALUCOLOR POLIESTER NEGRO MATE PP13", 2m, 20),
        8_102_000m),
    new CalibrationCase("V-23", "pricing puro defectuoso",
        new HistoricalCandidateQuery("VENTANA",
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
            "FIJO", null, null, 2.28m,
            "ALUCOLOR POLIESTER NEGRO MATE PP13", 1m, 20),
        1_075_000m),
    new CalibrationCase("PV-15", "area grande corregida",
        new HistoricalCandidateQuery("PUERTA",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
            "CORREDIZA", 5320m, 2500m, 13.30m,
            "ALUCOLOR POLIESTER NEGRO MATE PP13", 1m, 20),
        8_421_000m),
    new CalibrationCase("V-08", "error mixto TEMP_6",
        new HistoricalCandidateQuery("VENTANA",
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC", 6m,
            "FIJO", 3800m, 1900m, 7.22m,
            "ALUCOLOR POLIESTER NEGRO MATE PP13", 4m, 20),
        11_440_000m)
];

static void PrintQueryComparison(
    HistoricalCandidateQuery runtime,
    HistoricalCandidateQuery tool)
{
    Console.WriteLine("Field\tRuntime\tTool\tEqual");
    Print("Category", runtime.Category, tool.Category);
    Print("System", runtime.System, tool.System);
    Print("Glass", runtime.Glass, tool.Glass);
    Print("GlassThickness", runtime.GlassThickness, tool.GlassThickness);
    Print("GlassComposition", runtime.GlassComposition, tool.GlassComposition);
    Print("Configuration", runtime.Configuration, tool.Configuration);
    Print("Width", runtime.Width, tool.Width);
    Print("Height", runtime.Height, tool.Height);
    Print("Area", runtime.Area, tool.Area);
    Print("Finish", runtime.Finish, tool.Finish);
    Print("Quantity", runtime.Quantity, tool.Quantity);
    Print("TopK", runtime.Top, tool.Top);
    Print("ExcludedQuoteIds", Join(runtime.ExcludedQuoteIds), Join(tool.ExcludedQuoteIds));
    Print("ExcludedCandidateIds", Join(runtime.ExcludedCandidateIds), Join(tool.ExcludedCandidateIds));

    static void Print<T>(string field, T? runtime, T? tool)
    {
        var equal = string.Equals(Convert.ToString(runtime), Convert.ToString(tool),
            StringComparison.Ordinal);
        Console.WriteLine($"{field}\t{runtime}\t{tool}\t{equal}");
    }

    static string Join(IReadOnlyCollection<string>? values) =>
        values is null ? "<null>" : string.Join(',', values.Order(StringComparer.Ordinal));
}

static async Task PrintStability(
    string reference,
    HistoricalCandidateQuery query,
    HistoricalComparableCandidateService candidateService,
    EvaluateHistoricalSimilarityService similarityService)
{
    var runs = new List<ReproRun>();
    for (var index = 1; index <= 3; index++)
    {
        var top = candidateService.Find(query);
        var evaluation = await similarityService.EvaluateAsync(query);
        var estimator = new HistoricalTechnicalPriceEstimator(
            new FixedSimilarityEvaluationService(evaluation));
        var estimate = await estimator.EstimateAsync(query);
        var beforeIqr = BuildProjectedComparables(
            query,
            evaluation.Candidates,
            HistoricalTechnicalPricingRules.LowSimilarityWeightFactor);
        var afterIds = estimate.Comparables
            .Select(value => value.CandidateId)
            .ToArray();
        var excluded = beforeIqr
            .Select(value => value.Candidate.HistoricalItemId)
            .Where(value => !afterIds.Contains(value, StringComparer.Ordinal))
            .ToArray();
        var run = new ReproRun(
            index,
            top.Select(value => value.HistoricalItemId).ToArray(),
            evaluation.Candidates.Select(value => value.Candidate.HistoricalItemId).ToArray(),
            evaluation.Candidates.ToDictionary(
                value => value.Candidate.HistoricalItemId,
                value => value.Similarity?.SimilarityScore),
            evaluation.Candidates.ToDictionary(
                value => value.Candidate.HistoricalItemId,
                value => value.Similarity?.SimilarityLevel),
            beforeIqr.Select(value => value.Candidate.HistoricalItemId).ToArray(),
            excluded,
            afterIds,
            estimate.Minimum,
            estimate.Expected,
            estimate.Maximum,
            estimate.ConfidenceScore,
            estimate.ConfidenceLevel.ToString());
        runs.Add(run);

        Console.WriteLine($"--- {reference} RUN {index} ---");
        Console.WriteLine($"TopK={string.Join(',', run.TopKIds)}");
        Console.WriteLine($"Ai2Returned={string.Join(',', run.Ai2ReturnedIds)}");
        Console.WriteLine($"BeforeIqr={string.Join(',', run.BeforeIqrIds)}");
        Console.WriteLine($"ExcludedByIqr={string.Join(',', run.ExcludedByIqrIds)}");
        Console.WriteLine($"AfterIqr={string.Join(',', run.AfterIqrIds)}");
        Console.WriteLine($"Minimum={run.Minimum}; Expected={run.Expected}; Maximum={run.Maximum}; Confidence={run.ConfidenceScore} {run.ConfidenceLevel}");
        foreach (var candidateId in run.Ai2ReturnedIds)
        {
            Console.WriteLine($"AI2\t{candidateId}\t{run.Scores[candidateId]}\t{run.Levels[candidateId]}");
        }
    }

    var expected = runs.Select(value => value.Expected)
        .Where(value => value is not null)
        .Select(value => value.GetValueOrDefault())
        .ToArray();
    if (expected.Length > 0)
    {
        var min = expected.Min();
        var max = expected.Max();
        var mean = expected.Average();
        var rangePct = mean == 0 ? 0 : (max - min) / mean * 100m;
        Console.WriteLine($"STABILITY {reference}: expectedMinAcrossRuns={min}; expectedMaxAcrossRuns={max}; expectedMean={mean}; expectedRangePct={rangePct}; classification={Classify(rangePct)}");
    }

    Console.WriteLine($"TopKStable={runs.Select(value => string.Join(',', value.TopKIds)).Distinct(StringComparer.Ordinal).Count() == 1}");
    Console.WriteLine($"CandidateSetJaccard={Jaccard(runs.Select(value => (IReadOnlyList<string>)value.Ai2ReturnedIds).ToArray())}");
    Console.WriteLine($"AI2SimilarityStdDev={SimilarityStdDev(runs)}");
}

static async Task PrintDeterministicRequirementBaseline(
    Domain.PreQuotes.RequirementTechnicalProposal proposal,
    IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
    IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
    IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes,
    TechnicalProposalItemToHistoricalPricingMapper mapper,
    HistoricalQuoteCorpus corpus,
    HistoricalComparableCandidateService candidateService,
    IReadOnlyDictionary<string, CalibrationCase> knownCases)
{
    var fallbackSimilarity = new EvaluateHistoricalSimilarityService(
        corpus,
        candidateService,
        new FailingSimilarityClient("DETERMINISTIC_BASELINE_NO_AI2"));
    var estimator = new HistoricalTechnicalPriceEstimator(fallbackSimilarity);
    var rows = new List<BaselineRow>();

    Console.WriteLine("=== DETERMINISTIC REQUIREMENT BASELINE ===");
    Console.WriteLine("Reference\tExpectedUnit\tExpectedLine\tRealLine\tSignedErrorPct\tAbsoluteErrorPct");
    foreach (var proposalItem in proposal.Items.OrderBy(value => value.ExtractedItem.Sequence).ThenBy(value => value.Id))
    {
        if (proposalItem.SuggestedSystemId is not { } systemId
            || proposalItem.SuggestedGlassTypeId is not { } glassId
            || proposalItem.SuggestedFinishTypeId is not { } finishId
            || !systems.TryGetValue(systemId, out var system)
            || !glasses.TryGetValue(glassId, out var glass)
            || !finishes.TryGetValue(finishId, out var finish))
        {
            continue;
        }

        var mapping = mapper.Map(proposalItem, system, glass, finish);
        if (mapping.PricingArea is not > 0 || mapping.Quantity <= 0)
        {
            continue;
        }

        var estimate = await estimator.EstimateAsync(mapping.CandidateQuery);
        var line = estimate.Expected * mapping.Quantity;
        var reference = proposalItem.ExtractedItem.Reference ?? $"SEQ-{proposalItem.ExtractedItem.Sequence}";
        var real = knownCases.TryGetValue(reference, out var known)
            ? known.RealLinePrice
            : (decimal?)null;
        var signed = real is > 0 && line is not null
            ? (line.Value - real.Value) / real.Value * 100m
            : (decimal?)null;
        decimal? absolute = signed is null ? null : Math.Abs(signed.Value);
        rows.Add(new BaselineRow(reference, line, real, signed, absolute));
        Console.WriteLine($"{reference}\t{estimate.Expected}\t{line}\t{real?.ToString() ?? "<unknown>"}\t{signed?.ToString() ?? "<unknown>"}\t{absolute?.ToString() ?? "<unknown>"}");
    }

    var knownRows = rows.Where(value => value.AbsoluteErrorPct is not null)
        .ToArray();
    Console.WriteLine($"PriceableBaselineCount={rows.Count}");
    Console.WriteLine($"KnownRealPriceCount={knownRows.Length}");
    if (knownRows.Length == 0)
    {
        return;
    }

    var signedErrors = knownRows.Select(value => value.SignedErrorPct!.Value)
        .Order()
        .ToArray();
    var absoluteErrors = knownRows.Select(value => value.AbsoluteErrorPct!.Value)
        .Order()
        .ToArray();
    var subtotalExpected = knownRows.Sum(value => value.ExpectedLine ?? 0m);
    var subtotalReal = knownRows.Sum(value => value.RealLine ?? 0m);
    Console.WriteLine($"MedianAPE={Median(absoluteErrors)}");
    Console.WriteLine($"MAPE={absoluteErrors.Average()}");
    Console.WriteLine($"MeanSignedError={signedErrors.Average()}");
    Console.WriteLine($"MedianSignedError={Median(signedErrors)}");
    Console.WriteLine($"Within10={knownRows.Count(value => value.AbsoluteErrorPct <= 10m)}/{knownRows.Length}");
    Console.WriteLine($"Within15={knownRows.Count(value => value.AbsoluteErrorPct <= 15m)}/{knownRows.Length}");
    Console.WriteLine($"Within20={knownRows.Count(value => value.AbsoluteErrorPct <= 20m)}/{knownRows.Length}");
    Console.WriteLine($"Within25={knownRows.Count(value => value.AbsoluteErrorPct <= 25m)}/{knownRows.Length}");
    Console.WriteLine($"KnownSubtotalErrorPct={(subtotalExpected - subtotalReal) / subtotalReal * 100m}");
}

static Guid? ReadGuidArg(string[] args, string name)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.Ordinal));
    return index >= 0 && index + 1 < args.Length && Guid.TryParse(args[index + 1], out var value)
        ? value
        : null;
}

static string Classify(decimal rangePct) => rangePct switch
{
    <= 3m => "STABLE",
    <= 8m => "ACCEPTABLE_VARIATION",
    _ => "UNSTABLE_FOR_CALIBRATION"
};

static decimal Jaccard(IReadOnlyList<IReadOnlyList<string>> sets)
{
    if (sets.Count == 0) return 1m;
    var intersection = sets
        .Select(value => value.ToHashSet(StringComparer.Ordinal))
        .Aggregate((left, right) =>
        {
            left.IntersectWith(right);
            return left;
        });
    var union = sets
        .SelectMany(value => value)
        .ToHashSet(StringComparer.Ordinal);
    return union.Count == 0 ? 1m : (decimal)intersection.Count / union.Count;
}

static string SimilarityStdDev(IReadOnlyList<ReproRun> runs)
{
    var ids = runs.SelectMany(value => value.Scores.Keys)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var rows = new List<string>();
    foreach (var id in ids)
    {
        var scores = runs.Select(value =>
                value.Scores.TryGetValue(id, out var score) ? score : null)
            .Where(value => value is not null)
            .Select(value => value.GetValueOrDefault())
            .ToArray();
        if (scores.Length <= 1)
        {
            continue;
        }
        var mean = scores.Average();
        var variance = scores.Average(value => (value - mean) * (value - mean));
        rows.Add($"{id}:{(decimal)Math.Sqrt((double)variance)}");
    }
    return rows.Count == 0 ? "<none>" : string.Join('|', rows);
}

static async Task PrintCalibrationCase(
    CalibrationCase calibrationCase,
    HistoricalComparableCandidateService candidateService,
    EvaluateHistoricalSimilarityService similarityService,
    HistoricalTechnicalPriceEstimator estimator)
{
    var query = calibrationCase.Query;
    var allPricing = candidateService.Find(query with { Top = 100 });
    var top = candidateService.Find(query);
    var evaluation = await similarityService.EvaluateAsync(query);
    var estimate = await estimator.EstimateAsync(query);
    var rejected = evaluation.Candidates.Count(value =>
        value.Similarity?.SimilarityLevel.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) == true);
    var low = evaluation.Candidates.Count(value =>
        value.Similarity?.SimilarityLevel.Equals("LOW", StringComparison.OrdinalIgnoreCase) == true);
    var medium = evaluation.Candidates.Count(value =>
        value.Similarity?.SimilarityLevel.Equals("MEDIUM", StringComparison.OrdinalIgnoreCase) == true);
    var high = evaluation.Candidates.Count(value =>
        value.Similarity?.SimilarityLevel.Equals("HIGH", StringComparison.OrdinalIgnoreCase) == true);
    var beforeIqr = BuildProjectedComparables(query, evaluation.Candidates, lowFactor: 0.10m);
    var finalIds = estimate.Comparables.Select(value => value.CandidateId)
        .ToHashSet(StringComparer.Ordinal);
    var excludedByIqr = beforeIqr
        .Where(value => !finalIds.Contains(value.Candidate.HistoricalItemId))
        .ToArray();
    var realUnit = calibrationCase.RealLinePrice / (query.Quantity is > 0 ? query.Quantity.Value : 1m);
    var lineExpected = estimate.Expected * (query.Quantity is > 0 ? query.Quantity.Value : 1m);
    decimal? signedPct = lineExpected is null
        ? null
        : (lineExpected.Value - calibrationCase.RealLinePrice) / calibrationCase.RealLinePrice * 100m;

    Console.WriteLine($"=== CASE {calibrationCase.Name}: {calibrationCase.Notes} ===");
    Console.WriteLine($"Query category={query.Category}; system={query.System}; glass={query.Glass}; thickness={query.GlassThickness}; config={query.Configuration}; area={query.Area}; qty={query.Quantity}; finish={query.Finish}");
    Console.WriteLine($"Funnel corpus_items={candidateService.Find(query with { Top = 100 }).Count}; preliminary={allPricing.Count}; topK={top.Count}; similarityStatus={evaluation.Status}; rejected={rejected}; low={low}; medium={medium}; high={high}; pricingBeforeIqr={beforeIqr.Count}; iqrExcluded={excludedByIqr.Length}; final={estimate.Comparables.Count}");
    Console.WriteLine($"Estimate unit expected={estimate.Expected}; line expected={lineExpected}; real line={calibrationCase.RealLinePrice}; signedErrorPct={signedPct}; confidence={estimate.ConfidenceScore} {estimate.ConfidenceLevel}; requiresReview={estimate.RequiresReview}");
    Console.WriteLine($"IQR excluded ids={string.Join(',', excludedByIqr.Select(value => value.Candidate.HistoricalItemId))}");
    PrintComparableRows(query, estimate.Comparables, beforeIqr, realUnit);
    PrintAlternativeStatistics(query, beforeIqr, estimate.Comparables, realUnit);
    PrintSensitivity(query, evaluation.Candidates, realUnit);
}

static IReadOnlyList<ProjectedComparable> BuildProjectedComparables(
    HistoricalCandidateQuery query,
    IReadOnlyList<HistoricalSimilarityCandidateResult> candidates,
    decimal lowFactor)
{
    var values = new List<ProjectedComparable>();
    foreach (var value in candidates)
    {
        var candidate = value.Candidate;
        var historicalUnitArea = HistoricalTechnicalPriceEstimator.ResolveHistoricalUnitArea(candidate);
        var newArea = query.Area ?? GeometryArea(query.Width, query.Height);
        if (candidate.PublicUnitPrice <= 0 || historicalUnitArea is not > 0 || newArea is not > 0)
        {
            continue;
        }

        var similarity = value.Similarity;
        if (similarity?.SimilarityLevel.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) == true)
        {
            continue;
        }

        var backendScore = Math.Clamp(
            candidate.PreliminaryScore / HistoricalCandidateRankingWeights.MaximumScore,
            0m,
            1m);
        var ai2Score = similarity?.SimilarityScore;
        var weight = ai2Score is null ? backendScore * 0.40m : backendScore * Math.Clamp(ai2Score.Value, 0m, 1m);
        if (similarity?.SimilarityLevel.Equals("LOW", StringComparison.OrdinalIgnoreCase) == true)
        {
            weight *= lowFactor;
        }
        if (candidate.HasAreaMismatch)
        {
            weight *= 0.25m;
        }
        if (weight <= 0)
        {
            continue;
        }

        var projection = Project(candidate.PublicUnitPrice, historicalUnitArea.Value, newArea.Value);
        values.Add(new ProjectedComparable(candidate, similarity, historicalUnitArea.Value,
            newArea.Value, backendScore, weight, projection));
    }

    return values;
}

static void PrintComparableRows(
    HistoricalCandidateQuery query,
    IReadOnlyList<HistoricalTechnicalPriceComparable> finalComparables,
    IReadOnlyList<ProjectedComparable> beforeIqr,
    decimal realUnit)
{
    var projectedById = beforeIqr.ToDictionary(value => value.Candidate.HistoricalItemId, StringComparer.Ordinal);
    var totalWeight = finalComparables.Sum(value => value.FinalWeight);
    var strongExtrapolationWeight = finalComparables
        .Where(value => projectedById.TryGetValue(value.CandidateId, out var projected)
            && (projected.AreaRatio > 1.5m || projected.AreaRatio < 0.67m))
        .Sum(value => value.FinalWeight);
    var exactSystemWeight = finalComparables
        .Where(value => projectedById.TryGetValue(value.CandidateId, out var projected)
            && TextRelated(query.System, projected.Candidate.System))
        .Sum(value => value.FinalWeight);
    Console.WriteLine($"Extrapolation strong weight share={(totalWeight <= 0 ? 0 : strongExtrapolationWeight / totalWeight):P2}; exactSystemWeightShare={(totalWeight <= 0 ? 0 : exactSystemWeight / totalWeight):P2}; differentSystemWeightShare={(totalWeight <= 0 ? 0 : (totalWeight - exactSystemWeight) / totalWeight):P2}");
    Console.WriteLine("candidateId\thistoricalReference\tsystem\tcategory\tglass\tthickness\tfinish\twidth\theight\tarea\tqty\tpublicUnitPrice\tbackendScore\tai2\tsimLevel\tweight\tprojected\tareaRatio\tpriceM2\tprojected/real\tbranch");
    foreach (var comparable in finalComparables)
    {
        if (!projectedById.TryGetValue(comparable.CandidateId, out var projected))
        {
            continue;
        }
        var candidate = projected.Candidate;
        Console.WriteLine(string.Join('\t',
            comparable.CandidateId,
            comparable.HistoricalReference,
            candidate.System,
            candidate.Category,
            candidate.Glass,
            candidate.GlassThickness,
            candidate.Finish,
            candidate.Width,
            candidate.Height,
            candidate.Area,
            candidate.Quantity,
            comparable.PublicUnitPrice,
            comparable.BackendTechnicalScore,
            comparable.Ai2SimilarityScore,
            comparable.SimilarityLevel,
            comparable.FinalWeight,
            comparable.ProjectedPrice,
            projected.AreaRatio,
            comparable.PublicUnitPrice / projected.HistoricalUnitArea,
            comparable.ProjectedPrice / realUnit,
            projected.Projection.Branch));
    }
}

static void PrintAlternativeStatistics(
    HistoricalCandidateQuery query,
    IReadOnlyList<ProjectedComparable> beforeIqr,
    IReadOnlyList<HistoricalTechnicalPriceComparable> finalComparables,
    decimal realUnit)
{
    var finalValues = finalComparables
        .Select(value => new HistoricalWeightedValue(value.ProjectedPrice, value.FinalWeight))
        .ToArray();
    if (finalValues.Length == 0)
    {
        Console.WriteLine("Quantiles: no final comparables.");
        return;
    }

    var unweighted = finalComparables.Select(value => value.ProjectedPrice).Order().ToArray();
    var highMedium = finalComparables
        .Where(value => value.SimilarityLevel is "HIGH" or "MEDIUM")
        .Select(value => value.ProjectedPrice).Order().ToArray();
    var sameSystem = finalComparables
        .Where(value => beforeIqr.Any(projected => projected.Candidate.HistoricalItemId == value.CandidateId
            && TextRelated(query.System, projected.Candidate.System)))
        .Select(value => value.ProjectedPrice).Order().ToArray();
    Console.WriteLine($"Quantiles Q25={Weighted(finalValues, 0.25m)} Q50={Weighted(finalValues, 0.50m)} Q75={Weighted(finalValues, 0.75m)} weightedMean={WeightedMean(finalComparables)} unweightedMedian={Median(unweighted)} highMediumMedian={Median(highMedium)} sameSystemMedian={Median(sameSystem)} realUnit={realUnit}");
}

static void PrintSensitivity(
    HistoricalCandidateQuery query,
    IReadOnlyList<HistoricalSimilarityCandidateResult> candidates,
    decimal realUnit)
{
    foreach (var factor in new[] { 0.10m, 0.05m, 0.00m })
    {
        var values = BuildProjectedComparables(query, candidates, factor)
            .Select(value => new HistoricalWeightedValue(value.Projection.ProjectedPrice, value.Weight))
            .ToArray();
        var expected = values.Length == 0 ? (decimal?)null : Weighted(values, 0.50m);
        Console.WriteLine($"LowSimilarityFactor={factor}; expected={expected}; expected/realUnit={(expected is null ? null : expected / realUnit)}");
    }
}

static Projection Project(decimal publicUnitPrice, decimal historicalUnitArea, decimal newUnitArea)
{
    var normalized = publicUnitPrice / historicalUnitArea * newUnitArea;
    var difference = Math.Abs(historicalUnitArea - newUnitArea) / Math.Max(Math.Abs(newUnitArea), 0.0001m);
    if (difference <= 0.10m)
    {
        return new Projection(difference, normalized, "75_DIRECT_25_NORMALIZED",
            publicUnitPrice * 0.75m + normalized * 0.25m);
    }
    if (difference <= 0.25m)
    {
        return new Projection(difference, normalized, "40_DIRECT_60_NORMALIZED",
            publicUnitPrice * 0.40m + normalized * 0.60m);
    }
    return new Projection(difference, normalized, "100_NORMALIZED", normalized);
}

static decimal? GeometryArea(decimal? width, decimal? height) =>
    width is > 0 && height is > 0
        ? (width > 50m || height > 50m ? width.Value * height.Value / 1_000_000m : width.Value * height.Value)
        : null;

static decimal Weighted(IReadOnlyList<HistoricalWeightedValue> values, decimal quantile) =>
    HistoricalTechnicalPriceStatistics.WeightedQuantile(values, quantile);

static decimal WeightedMean(IReadOnlyList<HistoricalTechnicalPriceComparable> values) =>
    values.Sum(value => value.ProjectedPrice * value.FinalWeight) / values.Sum(value => value.FinalWeight);

static decimal? Median(IReadOnlyList<decimal> values)
{
    if (values.Count == 0) return null;
    var middle = values.Count / 2;
    return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2m;
}

static bool TextRelated(string? expected, string? actual)
{
    var left = HistoricalQuoteNormalizer.NormalizeText(expected);
    var right = HistoricalQuoteNormalizer.NormalizeText(actual);
    return !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && (left == right
            || left.Contains(right, StringComparison.Ordinal)
            || right.Contains(left, StringComparison.Ordinal));
}

static void PrintEconomyOfScale(HistoricalCorpusSnapshot snapshot)
{
    Console.WriteLine("=== ECONOMY OF SCALE BACKEND-ONLY ===");
    var groups = snapshot.Quotes.SelectMany(quote => quote.Items)
        .Where(item => item.PublicUnitPrice is > 0
            && HistoricalTechnicalPriceEstimator.ResolveHistoricalUnitArea(new HistoricalComparableCandidate(
                "", item.Id, item.Reference, item.Description, item.PublicUnitPrice!.Value,
                item.PublicTotal, item.Category, item.SystemNormalized, item.GlassFamily,
                item.GlassThickness, item.GlassComposition, item.ConfigurationNormalized,
                item.Width, item.Height, item.ReportedArea ?? item.DerivedArea, item.Quantity,
                item.FinishNormalized, 0m, [], [], false)) is > 0)
        .GroupBy(item => $"{item.Category}|{Family(item.SystemNormalized)}", StringComparer.Ordinal)
        .Where(group => group.Count() >= 8)
        .OrderByDescending(group => group.Count())
        .Take(12);
    Console.WriteLine("group\tn\tmedianArea\tmedianUnitPrice\tp25PriceM2\tp50PriceM2\tp75PriceM2\tcorrAreaPriceM2");
    foreach (var group in groups)
    {
        var rows = group.Select(item =>
        {
            var candidate = new HistoricalComparableCandidate(
                "", item.Id, item.Reference, item.Description, item.PublicUnitPrice!.Value,
                item.PublicTotal, item.Category, item.SystemNormalized, item.GlassFamily,
                item.GlassThickness, item.GlassComposition, item.ConfigurationNormalized,
                item.Width, item.Height, item.ReportedArea ?? item.DerivedArea, item.Quantity,
                item.FinishNormalized, 0m, [], [], false);
            var area = HistoricalTechnicalPriceEstimator.ResolveHistoricalUnitArea(candidate)!.Value;
            return (Area: area, Price: item.PublicUnitPrice!.Value, PriceM2: item.PublicUnitPrice!.Value / area);
        }).OrderBy(value => value.Area).ToArray();
        var priceM2 = rows.Select(value => value.PriceM2).Order().ToArray();
        Console.WriteLine(string.Join('\t', group.Key, rows.Length,
            Median(rows.Select(value => value.Area).Order().ToArray()),
            Median(rows.Select(value => value.Price).Order().ToArray()),
            Quantile(priceM2, 0.25m), Quantile(priceM2, 0.50m), Quantile(priceM2, 0.75m),
            Correlation(rows.Select(value => value.Area).ToArray(), rows.Select(value => value.PriceM2).ToArray())));
    }
}

static string Family(string? system)
{
    var text = HistoricalQuoteNormalizer.NormalizeText(system) ?? "";
    foreach (var token in new[] { "NAPOLES", "MONZA", "FERMO", "SIENA", "LAGO" })
    {
        if (text.Contains(token, StringComparison.Ordinal)) return token;
    }
    return "OTHER";
}

static decimal Quantile(IReadOnlyList<decimal> values, decimal quantile)
{
    if (values.Count == 0) return 0m;
    var index = (int)Math.Floor((values.Count - 1) * quantile);
    return values[Math.Clamp(index, 0, values.Count - 1)];
}

static decimal? Correlation(IReadOnlyList<decimal> xs, IReadOnlyList<decimal> ys)
{
    if (xs.Count < 2 || xs.Count != ys.Count) return null;
    var avgX = xs.Average();
    var avgY = ys.Average();
    var numerator = xs.Zip(ys, (x, y) => (x - avgX) * (y - avgY)).Sum();
    var denominator = (decimal)Math.Sqrt((double)(xs.Sum(x => (x - avgX) * (x - avgX)) * ys.Sum(y => (y - avgY) * (y - avgY))));
    return denominator == 0 ? null : numerator / denominator;
}

static void PrintValidationDesign()
{
    Console.WriteLine("=== VALIDATION DESIGN ===");
    Console.WriteLine("Use Leave-One-Quote-Out when AI2 budget allows; otherwise Group K-Fold with HistoricalQuoteId as group.");
    Console.WriteLine("Metrics: item APE, signed error, Median APE, MAPE, P75/P90 APE, within 10/15/20/25, quote-level total error, segmented by function/system/area/confidence.");
    Console.WriteLine("Recommended PRICING.2B candidates: HIGH=area extrapolation dampening; MEDIUM=exact-system/family-aware weighting; EXPERIMENTAL=power-law alpha learned via grouped CV.");
}

public sealed record CalibrationCase(
    string Name,
    string Notes,
    HistoricalCandidateQuery Query,
    decimal RealLinePrice);

public sealed record BaselineRow(
    string Reference,
    decimal? ExpectedLine,
    decimal? RealLine,
    decimal? SignedErrorPct,
    decimal? AbsoluteErrorPct);

public sealed record ProjectedComparable(
    HistoricalComparableCandidate Candidate,
    SimilarityCandidateResult? Similarity,
    decimal HistoricalUnitArea,
    decimal NewUnitArea,
    decimal BackendScore,
    decimal Weight,
    Projection Projection)
{
    public decimal AreaRatio => NewUnitArea / HistoricalUnitArea;
}

public sealed record Projection(
    decimal RelativeDifference,
    decimal NormalizedPrice,
    string Branch,
    decimal ProjectedPrice);

public sealed record ReproRun(
    int Index,
    IReadOnlyList<string> TopKIds,
    IReadOnlyList<string> Ai2ReturnedIds,
    IReadOnlyDictionary<string, decimal?> Scores,
    IReadOnlyDictionary<string, string?> Levels,
    IReadOnlyList<string> BeforeIqrIds,
    IReadOnlyList<string> ExcludedByIqrIds,
    IReadOnlyList<string> AfterIqrIds,
    decimal? Minimum,
    decimal? Expected,
    decimal? Maximum,
    decimal ConfidenceScore,
    string ConfidenceLevel);

public sealed class FixedSimilarityEvaluationService(
    HistoricalSimilarityEvaluationResult result)
    : IHistoricalSimilarityEvaluationService
{
    public Task<HistoricalSimilarityEvaluationResult> EvaluateAsync(
        HistoricalCandidateQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(result);
}

public sealed class FailingSimilarityClient(string failureCode) : IAi2SimilarityClient
{
    public Task<Ai2SimilarityClientResult> EvaluateAsync(
        SimilarityEvaluationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Ai2SimilarityClientResult.Failed(failureCode));
}
