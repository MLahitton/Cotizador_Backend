using Application.Common.Abstractions.PreQuotes;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class PreQuoteDraftRepositoryTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindSourceAsync_Schema3_ReturnsGlassDetection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: true,
            includeValuation: false,
            requiresReview: true);

        await using var context = fixture.CreateDbContext();
        var repository = new PreQuoteDraftRepository(context);
        var source = await repository.FindSourceAsync(
            seeded.PreQuoteId,
            seeded.DocumentId,
            seeded.ExtractionId,
            seeded.OwnerUserId,
            cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        var glass = item.Glass;
        Assert.NotNull(glass);
        Assert.Equal(seeded.GlassTypeId, glass.GlassTypeId);
        Assert.Equal("LAM_4_4", glass.NormalizedCodeSnapshot);
        Assert.Equal("Laminado 4+4", glass.RawSpecification);
        Assert.Equal(GlassAssignmentScope.Item, glass.AssignmentScope);
        Assert.True(glass.RequiresReview);
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_ReturnsReviewReasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(includeGlass: true, includeValuation: false, requiresReview: true);
        await using var context = fixture.CreateDbContext();

        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        var glass = item.Glass;
        Assert.NotNull(glass);
        Assert.Equal(
            [GlassReviewReason.GlassTypeNotIdentified, GlassReviewReason.GlassTypeConflict],
            glass.ReviewReasons);
        Assert.True(glass.ReviewReasons.SequenceEqual(new[]
        {
            GlassReviewReason.GlassTypeNotIdentified,
            GlassReviewReason.GlassTypeConflict
        }));
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_ReturnsSourcePages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: true,
            includeValuation: false,
            includesSourcePages: [1, 2, 3],
            requiresReview: true);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        var glass = item.Glass;
        Assert.NotNull(glass);
        Assert.Equal([1, 2, 3], glass.SourcePages.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_ReturnsEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: true,
            includeValuation: false,
            requiresReview: true,
            includesSourcePages: [1, 2],
            evidence: [
                new StructuredItemGlassEvidenceInput(1, 1, EvidenceSourceType.Native, "Evidence #1"),
                new StructuredItemGlassEvidenceInput(2, 2, EvidenceSourceType.Native, "Evidence #2")
            ]);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        var glass = item.Glass;
        Assert.NotNull(glass);
        var evidence = glass.Evidence.OrderBy(x => x.Sequence).ToArray();

        Assert.Equal(2, evidence.Length);
        Assert.Equal(1, evidence[0].Sequence);
        Assert.Equal(1, evidence[0].PageNumber);
        Assert.Equal("Evidence #1", evidence[0].Text);
        Assert.Equal(2, evidence[1].Sequence);
        Assert.Equal(2, evidence[1].PageNumber);
        Assert.Equal("Evidence #2", evidence[1].Text);
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_ReturnsValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: true,
            includeValuation: true,
            requiresReview: false);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        var valuation = item.Valuation;
        Assert.NotNull(valuation);
        Assert.Equal(PreQuoteDraftValuationStatus.Valued, valuation.Status);
        Assert.Equal(seeded.PriceRangeId, valuation.GlassPriceRangeVersionId);
        Assert.Equal(1_000, valuation.WidthMillimetersUsed);
        Assert.Equal(1_500, valuation.HeightMillimetersUsed);
        Assert.Equal(3, valuation.QuantityUsed);
        Assert.Equal(1.500000m, valuation.UnitAreaSquareMeters);
        Assert.Equal(4.500000m, valuation.TotalAreaSquareMeters);
        Assert.Equal(90_000.12m, valuation.UnitPricePerSquareMeter);
        Assert.Equal(270_000.37m, valuation.UnitAmount);
        Assert.Equal(810_001.11m, valuation.TotalAmount);
        Assert.Equal("COP", valuation.Currency);
        Assert.Equal(At.AddMinutes(2), valuation.ValuedAtUtc);
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_WithoutGlass_ReturnsNullGlass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(includeGlass: false, includeValuation: false);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        Assert.Null(item.Glass);
    }

    [Fact]
    public async Task FindSourceAsync_Schema3_WithoutValuation_ReturnsNullValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: true,
            includeValuation: false);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        Assert.NotNull(item.Glass);
        Assert.Null(item.Valuation);
    }

    [Fact]
    public async Task FindSourceAsync_Schema2_ReturnsHistoricalSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(
            includeGlass: false,
            includeValuation: false,
            schemaVersion: "2.0");

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                seeded.OwnerUserId,
                cancellationToken);

        Assert.NotNull(source);
        var item = Assert.Single(source!.Items);
        Assert.Null(item.Glass);
        Assert.Null(item.Valuation);
    }

    [Fact]
    public async Task FindSourceAsync_ForeignOwner_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedSourceAsync(includeGlass: true, includeValuation: true, requiresReview: false);

        await using var context = fixture.CreateDbContext();
        var source = await new PreQuoteDraftRepository(context)
            .FindSourceAsync(
                seeded.PreQuoteId,
                seeded.DocumentId,
                seeded.ExtractionId,
                Guid.NewGuid(),
                cancellationToken);

        Assert.Null(source);
    }

    private async Task<SeededExtraction> SeedSourceAsync(
        bool includeGlass,
        bool includeValuation,
        string schemaVersion = "3.0",
        bool requiresReview = false,
        int[]? includesSourcePages = null,
        StructuredItemGlassEvidenceInput[]? evidence = null)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var owner = User.CreateFromGoogle(
            "owner@example.com",
            "Owner",
            "A",
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Synthetic Client",
            null,
            null,
            null,
            null,
            null,
            null,
            "Bogota",
            owner.Id,
            At);
        var project = Project.Create(
            client.Id,
            "PRJ-1",
            "Project",
            null,
            "Bogota",
            owner.Id,
            At);
        var preQuote = PreQuote.Create(project.Id, owner.Id, At);
        var document = PreQuoteDocument.Create(
            preQuote.Id,
            "document.pdf",
            "application/pdf",
            11_520,
            "prequotes/document.pdf",
            owner.Id,
            At);
        var attempt = DocumentProcessingAttempt.Create(
            document.Id,
            owner.Id,
            Guid.NewGuid(),
            At);
        attempt.Start(At.AddMinutes(1));
        attempt.Complete(DocumentProcessingOutcome.Completed, At.AddMinutes(3));

        var result = DocumentExtractionResult.Create(
            attempt.Id,
            schemaVersion,
            PdfClassification.PdfText,
            false,
            5,
            "pymupdf",
            15,
            "{\"a\":1}",
            At.AddMinutes(2));

        Guid? glassTypeId = null;
        Guid? priceRangeId = null;
        if (includeGlass)
        {
            var glassType = GlassType.Create(
                "LAM_4_4",
                "Laminado 4+4",
                null,
                At);
            context.Add(glassType);
            glassTypeId = glassType.Id;

            if (includeValuation)
            {
                var priceRange = GlassPriceRangeVersion.Create(
                    glassType.Id,
                    1,
                    90_000.123456m,
                    110_000.123456m,
                    "COP",
                    GlassPriceRangeStatus.Preliminary,
                    At,
                    null,
                    At);
                context.Add(priceRange);
                priceRangeId = priceRange.Id;
            }
        }

        var sourcePages = includesSourcePages?.ToArray() ?? new[] { 1 };
        var sourceEvidence = evidence
            ?? sourcePages
                .Select((page, index) => new StructuredItemGlassEvidenceInput(
                    index + 1,
                    page,
                    EvidenceSourceType.Native,
                    $"Evidence #{page}"))
                .ToArray();

        GlassReviewReason[] reviewReasons = requiresReview
            ? new[]
            {
                GlassReviewReason.GlassTypeNotIdentified,
                GlassReviewReason.GlassTypeConflict
            }
            : Array.Empty<GlassReviewReason>();
        var valuation = includeValuation && includeGlass && priceRangeId.HasValue
            && glassTypeId.HasValue
            ? new StructuredItemGlassValuationInput(
                GlassValuationStatus.Valued,
                null,
                glassTypeId,
                priceRangeId,
                1,
                GlassPriceRangeStatus.Preliminary,
                "COP",
                1.500000m,
                4.500000m,
                90_000.123456m,
                110_000.123456m,
                270_000.370368m,
                810_001.111104m)
            : null;
        var extraction = StructuredDocumentExtraction.Create(
            result.Id,
            StructuredExtractionStatus.Completed,
            "Synthetic project",
            "Synthetic client",
            "Bogota",
            1,
            0,
            requiresReview ? 1 : 0,
            3,
            "rule_based_v2",
            5,
            [
                new StructuredItemInput(
                    1,
                    "W-01",
                    "Synthetic window",
                    StructuredElementType.Window,
                    "1000 x 1500 mm",
                    1000,
                    1500,
                    3,
                    requiresReview,
                    includeGlass
                        ? new StructuredItemGlassInput(
                            glassTypeId,
                            "Laminado 4+4",
                            "LAM_4_4",
                            GlassAssignmentScope.Item,
                            requiresReview,
                            reviewReasons,
                            sourcePages,
                            sourceEvidence)
            : null,
                    valuation)
            ],
            [],
            [],
            [],
            [],
            At.AddMinutes(2),
            includeGlass ? 1 : 0,
            requiresReview ? 1 : 0);

        context.AddRange(owner, client, project, preQuote, document, attempt,
            result, extraction);
        await context.SaveChangesAsync();

        return new SeededExtraction(
            owner.Id,
            preQuote.Id,
            document.Id,
            extraction.Id,
            glassTypeId,
            priceRangeId);
    }

    private sealed record SeededExtraction(
        Guid OwnerUserId,
        Guid PreQuoteId,
        Guid DocumentId,
        Guid ExtractionId,
        Guid? GlassTypeId,
        Guid? PriceRangeId);
}
