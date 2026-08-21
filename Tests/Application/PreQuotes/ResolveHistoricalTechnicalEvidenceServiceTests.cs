using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ResolveHistoricalTechnicalEvidenceServiceTests
{
    private static readonly HistoricalCandidateQuery Query = new(
        "VENTANA", "8025", "TEMPLADO", 6m, "CORREDIZA",
        1200m, 1500m, 1.8m, "NEGRO", 1m, 5);

    [Fact]
    public async Task ResolveAsync_HistoricalSimilarityDoesNotReviveIncompatibleSystem()
    {
        var service = CreateService(
            [
                System("S35", "PROJECTING", "PRIMAVERA SIENA",
                    technicalName: "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA"),
                System("K40", "FIXED", "VENECIA FERMO",
                    technicalName: "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO")
            ],
            Completed([
                Candidate("cand-fermo", "K40", "V-01",
                    "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
                    0.99m)
            ]));

        var result = await service.ResolveAsync(
            Input(functionalType: "PROJECTING"),
            Query,
            TestContext.Current.CancellationToken);

        Assert.Equal("S35", result.Selection.SuggestedSystemCode);
        Assert.Equal(0, result.Selection.HistoricalSupportCount);
        Assert.Single(result.HistoricalEvidence);
        Assert.Equal("K40", result.HistoricalEvidence[0].ProductSystemCode);
    }

    [Fact]
    public async Task ResolveAsync_HistoricalSimilarityCanBreakTieBetweenCompatibleSystems()
    {
        var service = CreateService(
            [
                System("A_PERGOLA_ALT", "PERGOLA", "PERGOLA X",
                    technicalName: "SISTEMA PERGOLA X"),
                System("SG_PERGOLA", "PERGOLA", "PERGOLA SG",
                    technicalName: "SISTEMA PERGOLA SG")
            ],
            Completed([
                Candidate("cand-pergola", "SG_PERGOLA", "P-01",
                    "SISTEMA PERGOLA SG",
                    0.96m)
            ]));

        var result = await service.ResolveAsync(
            Input(functionalType: "PERGOLA"),
            Query,
            TestContext.Current.CancellationToken);

        Assert.Equal("SG_PERGOLA", result.Selection.SuggestedSystemCode);
        Assert.Equal(1, result.Selection.HistoricalSupportCount);
        Assert.Equal(0.96m, result.Selection.HistoricalBestSimilarity);
        Assert.Single(result.Selection.HistoricalExamples!);
    }

    [Fact]
    public async Task ResolveAsync_WhenSimilarityFails_FallsBackToDeterministicSelection()
    {
        var service = CreateService(
            [
                System("S35", "PROJECTING", "PRIMAVERA SIENA",
                    technicalName: "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA"),
                System("K40", "FIXED", "VENECIA FERMO",
                    technicalName: "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO")
            ],
            new HistoricalSimilarityEvaluationResult(
                HistoricalSimilarityStatus.TechnicalFailure,
                [Candidate("cand-fermo", "K40", "V-01", "VENECIA FERMO", null)],
                "AI2_SIMILARITY_TRANSPORT_ERROR"));

        var result = await service.ResolveAsync(
            Input(functionalType: "PROJECTING"),
            Query,
            TestContext.Current.CancellationToken);

        Assert.Equal(HistoricalSimilarityStatus.TechnicalFailure,
            result.SimilarityStatus);
        Assert.Equal("AI2_SIMILARITY_TRANSPORT_ERROR",
            result.SimilarityFailureCode);
        Assert.Equal("S35", result.Selection.SuggestedSystemCode);
        Assert.Empty(result.HistoricalEvidence);
    }

    [Fact]
    public async Task ResolveAsync_HistoricalSimilarityDoesNotRemoveMandatoryReview()
    {
        var service = CreateService(
            [
                System("S50", "SLIDING_WINDOW", "PRIMAVERA LAGO",
                    technicalName: "VENTANA CORREDIZA LINEA CLASSIC PRIMAVERA LAGO"),
                System("K50", "SLIDING_WINDOW", "VENECIA MONZA",
                    technicalName: "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA")
            ],
            Completed([
                Candidate("cand-monza", "K50", "V-02",
                    "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA",
                    0.93m)
            ]));

        var result = await service.ResolveAsync(
            Input(functionalType: "SLIDING_WINDOW", geometryType: "CORNER"),
            Query,
            TestContext.Current.CancellationToken);

        Assert.Equal("K50", result.Selection.SuggestedSystemCode);
        Assert.True(result.Selection.RequiresReview);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons.SpecialGeometryWithoutConstraints,
            result.Selection.ReviewReasons);
        Assert.Equal(1, result.Selection.HistoricalSupportCount);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresOutOfRangeSimilarityScore()
    {
        var service = CreateService(
            [
                System("A_PERGOLA_ALT", "PERGOLA", "PERGOLA X",
                    technicalName: "SISTEMA PERGOLA X"),
                System("SG_PERGOLA", "PERGOLA", "PERGOLA SG",
                    technicalName: "SISTEMA PERGOLA SG")
            ],
            Completed([
                Candidate("cand-pergola", "SG_PERGOLA", "P-01",
                    "SISTEMA PERGOLA SG",
                    1.2m)
            ]));

        var result = await service.ResolveAsync(
            Input(functionalType: "PERGOLA"),
            Query,
            TestContext.Current.CancellationToken);

        Assert.Null(result.Selection.SuggestedSystemCode);
        Assert.Empty(result.HistoricalEvidence);
    }

    private static ResolveHistoricalTechnicalEvidenceService CreateService(
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        HistoricalSimilarityEvaluationResult similarityResult)
    {
        var similarity = Substitute.For<IHistoricalSimilarityEvaluationService>();
        similarity.EvaluateAsync(Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(similarityResult);
        var catalog = new Catalog(systems);
        return new ResolveHistoricalTechnicalEvidenceService(
            similarity,
            catalog,
            new DeterministicSgTechnicalSelector(catalog));
    }

    private static HistoricalSimilarityEvaluationResult Completed(
        IReadOnlyList<HistoricalSimilarityCandidateResult> candidates) =>
        new(HistoricalSimilarityStatus.Completed, candidates, null);

    private static HistoricalSimilarityCandidateResult Candidate(
        string candidateId,
        string systemCode,
        string reference,
        string system,
        decimal? similarityScore)
    {
        var candidate = new HistoricalComparableCandidate(
            "quote-1",
            candidateId,
            reference,
            "Historico",
            1000000m,
            1000000m,
            "VENTANA",
            system,
            "TEMPLADO",
            6m,
            "MONOLITICO",
            "CORREDIZA",
            1200m,
            1500m,
            1.8m,
            1m,
            "NEGRO",
            90m,
            ["system"],
            [],
            false);

        return new HistoricalSimilarityCandidateResult(
            candidate,
            similarityScore is null
                ? null
                : new SimilarityCandidateResult(
                    candidateId,
                    similarityScore.Value,
                    "HIGH",
                    ["system"],
                    [],
                    $"Soporta {systemCode}.",
                    0.9m));
    }

    private static SgTechnicalSelectionInput Input(
        string functionalType,
        string? geometryType = null) =>
        new(
            functionalType,
            null,
            1200,
            1500,
            1.8m,
            null,
            null,
            null,
            null,
            null,
            [],
            geometryType,
            null,
            null);

    private static ProductSystemCatalogReadModel System(
        string code,
        string functionalType,
        string? family,
        string technicalName) =>
        new(
            Guid.NewGuid(),
            code,
            technicalName,
            technicalName,
            family,
            functionalType,
            family,
            null,
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private sealed class Catalog(
        IReadOnlyList<ProductSystemCatalogReadModel> systems)
        : IProductSystemCatalogRepository
    {
        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<IReadOnlyList<ProductSystemCatalogReadModel>>
            ListActiveSelectableAsync(CancellationToken cancellationToken) =>
            Task.FromResult(systems);

        public Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(systems.SingleOrDefault(system =>
                system.Code == code));
    }
}
