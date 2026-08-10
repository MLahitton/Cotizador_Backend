using System.Text.Json.Nodes;
using Application.Common.Abstractions.PreQuotes;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class StructuredItemGlassValuationPostgreSqlTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valuation_RoundTripsExactSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync(includeValuation: true);
        await using var context = fixture.CreateDbContext();

        var value = await context.StructuredExtractionItemGlassValuations
            .AsNoTracking().SingleAsync(cancellationToken);

        Assert.Equal(GlassValuationStatus.Valued, value.Status);
        Assert.Null(value.Reason);
        Assert.Equal(seeded.GlassTypeId, value.GlassTypeId);
        Assert.Equal(seeded.PriceRangeId, value.GlassPriceRangeVersionId);
        Assert.Equal(1, value.PriceRangeVersion);
        Assert.Equal(GlassPriceRangeStatus.Preliminary, value.PriceRangeStatus);
        Assert.Equal("COP", value.Currency);
        Assert.Equal(1.500000m, value.UnitAreaSquareMeters);
        Assert.Equal(4.500000m, value.TotalAreaSquareMeters);
        Assert.Equal(90000.00m, value.MinimumPricePerSquareMeter);
        Assert.Equal(110000.00m, value.MaximumPricePerSquareMeter);
        Assert.Equal(405000.00m, value.MinimumAmount);
        Assert.Equal(495000.00m, value.MaximumAmount);
        Assert.Equal(TimeSpan.Zero, value.CalculatedAtUtc.Offset);
    }

    [Fact]
    public async Task Valuation_DuplicateItem_FailsPhysicalUniqueIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync(includeValuation: true);
        await using var context = fixture.CreateDbContext();
        var duplicateId = Guid.NewGuid();
        var postgres = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO core.structured_extraction_item_glass_valuations
                  (id, structured_extraction_item_id, status, reason,
                   glass_type_id, glass_price_range_version_id,
                   price_range_version, price_range_status, currency,
                   unit_area_square_meters, total_area_square_meters,
                   minimum_price_per_square_meter,
                   maximum_price_per_square_meter,
                   minimum_amount, maximum_amount, calculated_at_utc)
                SELECT {duplicateId}, structured_extraction_item_id, status,
                   reason, glass_type_id, glass_price_range_version_id,
                   price_range_version, price_range_status, currency,
                   unit_area_square_meters, total_area_square_meters,
                   minimum_price_per_square_meter,
                   maximum_price_per_square_meter,
                   minimum_amount, maximum_amount, calculated_at_utc
                FROM core.structured_extraction_item_glass_valuations
                WHERE structured_extraction_item_id = {seeded.ItemId}
                """, cancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.StartsWith(
            "IX_structured_extraction_item_glass_valuations_structured_extr",
            postgres.ConstraintName);

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(1, await verification
            .StructuredExtractionItemGlassValuations.CountAsync(
                cancellationToken));
    }

    [Fact]
    public async Task ReadRepository_PreservesHistoricalSnapshotAfterCatalogChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync(includeValuation: true);
        await using (var context = fixture.CreateDbContext())
        {
            await context.GlassPriceRangeVersions
                .Where(value => value.Id == seeded.PriceRangeId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.ValidToUtc,
                        At.AddDays(30)), cancellationToken);
            context.Add(GlassPriceRangeVersion.Create(
                seeded.GlassTypeId, 2, 200000m, 225000m, 250000m, "COP",
                GlassPriceRangeStatus.Preliminary, At.AddDays(30), null,
                At.AddDays(30)));
            await context.SaveChangesAsync(cancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var result = await new PreQuoteDocumentQueryRepository(readContext)
            .GetStructuredExtractionAsync(
                seeded.DocumentId, seeded.UserId, CancellationToken.None);
        Assert.NotNull(result);
        var valuation = Assert.Single(
            result.StructuredExtraction!.Items).Valuation;
        Assert.NotNull(valuation);
        Assert.Equal(1, valuation.PriceRangeVersion);
        Assert.Equal(90000.00m, valuation.MinimumPricePerSquareMeter);
        Assert.Equal(110000.00m, valuation.MaximumPricePerSquareMeter);
        Assert.Equal(405000.00m, valuation.MinimumAmount);
        Assert.Equal(495000.00m, valuation.MaximumAmount);
    }

    [Fact]
    public async Task ReadRepository_HistoricalWithoutValuation_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync(includeValuation: false);
        await using var context = fixture.CreateDbContext();
        var result = await new PreQuoteDocumentQueryRepository(context)
            .GetStructuredExtractionAsync(
                seeded.DocumentId, seeded.UserId, cancellationToken);
        Assert.NotNull(result);
        var item = Assert.Single(result.StructuredExtraction!.Items);
        Assert.Null(item.Valuation);
        Assert.Equal("W-01", item.Reference);
        Assert.Equal("Synthetic window", item.Description);
        Assert.Equal(1000, item.WidthMillimeters);
        Assert.Equal(1500, item.HeightMillimeters);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(seeded.GlassTypeId, item.Glass!.GlassTypeId);
        Assert.Equal("LAM_4_4", item.Glass.NormalizedCode);
    }

    [Fact]
    public async Task ReadRepository_ForeignOwner_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync(includeValuation: true);
        await using var context = fixture.CreateDbContext();
        var result = await new PreQuoteDocumentQueryRepository(context)
            .GetStructuredExtractionAsync(
                seeded.DocumentId, Guid.NewGuid(), cancellationToken);
        Assert.Null(result);
    }

    private async Task<Seeded> SeedAsync(bool includeValuation)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var user = User.CreateFromGoogle(
            "owner@example.com", "Owner", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, user.Id, At);
        var project = Project.Create(
            client.Id, "P-001", "Project", null, "Bogota", user.Id, At);
        var preQuote = PreQuote.Create(project.Id, user.Id, At);
        var document = PreQuoteDocument.Create(
            preQuote.Id, "document.pdf", "application/pdf", 4,
            "prequotes/document.pdf", user.Id, At);
        var attempt = DocumentProcessingAttempt.Create(
            document.Id, user.Id, Guid.NewGuid(), At);
        attempt.Start(At.AddMinutes(1));
        attempt.Complete(DocumentProcessingOutcome.Completed,
            At.AddMinutes(3));
        var payload = CreatePayload(document.Id, attempt.Id);
        var result = DocumentExtractionResult.Create(
            attempt.Id, "3.0", DocumentClassification.PdfText, false, 1,
            "pymupdf", 15, payload, At.AddMinutes(2));
        var glassType = GlassType.Create(
            "LAM_4_4", "Laminated 4+4", null, At);
        var priceRange = GlassPriceRangeVersion.Create(
            glassType.Id, 1, 90000m, 100000m, 110000m, "COP",
            GlassPriceRangeStatus.Preliminary, At, null, At);
        var valuation = includeValuation
            ? ValuationInput(glassType.Id, priceRange.Id, "COP")
            : null;
        var extraction = StructuredDocumentExtraction.Create(
            result.Id, StructuredExtractionStatus.Completed,
            "Synthetic project", "Synthetic client", "Bogota",
            1, 1, 0, 3, "rule_based_v2", 5,
            [new StructuredItemInput(
                1, "W-01", "Synthetic window",
                StructuredElementType.Window, "1000 x 1500 mm",
                1000, 1500, 3, false,
                new StructuredItemGlassInput(
                    glassType.Id, "Laminado 4+4", "LAM_4_4",
                    GlassAssignmentScope.Item, false, [], [1],
                    [new StructuredItemGlassEvidenceInput(
                        1, 1, EvidenceSourceType.Native,
                        "Synthetic evidence")]),
                valuation)],
            [new StructuredRequirementInput(
                1, RequirementCategory.GlassSpecification,
                "Tempered glass")],
            [new StructuredDocumentReferenceInput(
                1, "PLAN-01", "Synthetic drawing", "Reference only", 99)],
            [], [], At.AddMinutes(2), 1, 0);

        context.AddRange(user, client, project, preQuote, document, attempt,
            result, glassType, priceRange, extraction);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return new Seeded(user.Id, document.Id, glassType.Id, priceRange.Id,
            extraction.Items.Single().Id);
    }

    private static StructuredItemGlassValuationInput ValuationInput(
        Guid glassTypeId,
        Guid priceRangeId,
        string currency) => new(
            GlassValuationStatus.Valued, null, glassTypeId, priceRangeId, 1,
            GlassPriceRangeStatus.Preliminary, currency,
            1.500000m, 4.500000m, 90000.00m, 100000.00m, 110000.00m,
            405000.00m, 450000.00m, 495000.00m);

    private static string CreatePayload(Guid documentId, Guid attemptId)
    {
        var root = JsonNode.Parse(DocumentProcessingPayloadFactory.CreateSuccess(
            documentId, attemptId))!.AsObject();
        var structured = root["structuredExtraction"]!;
        var item = structured["items"]![0]!;
        item["rawMeasurements"] = "1000 x 1500 mm";
        item["widthMillimeters"] = 1000;
        item["heightMillimeters"] = 1500;
        item["quantity"] = 3;
        structured["summary"]!["knownQuoteableUnitCount"] = 3;
        return root.ToJsonString();
    }

    private sealed record Seeded(
        Guid UserId,
        Guid DocumentId,
        Guid GlassTypeId,
        Guid PriceRangeId,
        Guid ItemId);
}
